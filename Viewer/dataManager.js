// dataManager.js

export class DataManager {
  constructor() {
    this.parserWorker = new Worker("parser.js");
    this.datasetStructure = {};
    this.colorMap = this.createColorMap();
  }

  getSceneData(path) {
    const [room, scene, seed, promptName] = path;
    const seedData = this.datasetStructure[room][scene][seed];
    const prompt = seedData.prompts[promptName];
    return { room, scene, seed, promptName, seedData, prompt };
  }

  async buildDatasetStructure(files) {
    this.datasetStructure = {};
    const structure = {};
    for (const file of Array.from(files)) {
      const pathParts = file.webkitRelativePath.split("/");
      if (pathParts.length !== 6) continue;

      const [, room, scene, seed, subfolder, filename] = pathParts;

      if (!structure[room]) structure[room] = {};
      if (!structure[room][scene]) structure[room][scene] = {};
      if (!structure[room][scene][seed]) {
        structure[room][scene][seed] = { prompts: {}, staticCloudFile: null };
      }

      const seedData = structure[room][scene][seed];

      if (subfolder.toLowerCase() === "scene_point_cloud") {
        if (filename.toLowerCase().match(/\.(ply|xyz|pcd|txt)$/)) {
          seedData.staticCloudFile = file;
        }
      } else if (
        subfolder.startsWith("prompt_") ||
        subfolder.startsWith("sequence")
      ) {
        const promptName = subfolder;
        if (!seedData.prompts[promptName]) {
          seedData.prompts[promptName] = {
            pointCloudFiles: [],
            videoFile: null,
            descriptionFile: null,
            description: null,
          };
        }
        const promptData = seedData.prompts[promptName];
        if (filename.toLowerCase().match(/\.(ply|xyz|pcd|txt)$/)) {
          promptData.pointCloudFiles.push(file);
        } else if (filename.toLowerCase().endsWith(".mp4")) {
          promptData.videoFile = file;
        } else if (filename.toLowerCase().endsWith(".json")) {
          promptData.descriptionFile = file;
        }
      }
    }

    // Sort files numerically
    for (const room in structure) {
      for (const scene in structure[room]) {
        for (const seed in structure[room][scene]) {
          const seedData = structure[room][scene][seed];
          for (const prompt in seedData.prompts) {
            seedData.prompts[prompt].pointCloudFiles.sort((a, b) =>
              a.name.localeCompare(b.name, undefined, {
                numeric: true,
                sensitivity: "base",
              })
            );
          }
        }
      }
    }
    this.datasetStructure = structure;
    return this.datasetStructure;
  }

  async loadDescriptionForScene(path) {
    const sceneData = this.getSceneData(path);
    const promptData = sceneData.prompt;

    if (promptData.descriptionFile && !promptData.description) {
      try {
        const jsonContent = await promptData.descriptionFile.text();
        promptData.description =
          JSON.parse(jsonContent).description || "No description found.";
      } catch (e) {
        console.error(
          `Error parsing description for ${sceneData.promptName}:`,
          e
        );
        promptData.description = "Error reading description.";
      }
    }
  }

  async loadPointCloudData(sceneData) {
    const parseFile = (file) =>
      new Promise((resolve, reject) => {
        const messageId = Math.random();
        const handler = (event) => {
          if (event.data.id === messageId) {
            this.parserWorker.removeEventListener("message", handler);
            if (event.data.success) {
              resolve(event.data.payload);
            } else {
              reject(new Error(event.data.error));
            }
          }
        };
        this.parserWorker.addEventListener("message", handler);
        this.parserWorker.postMessage({ id: messageId, file: file });
      });

    const promises = sceneData.prompt.pointCloudFiles.map((file) =>
      parseFile(file).catch((e) => {
        console.error(`Failed to load frame ${file.name}:`, e);
        return null; // Don't let one bad file stop everything
      })
    );

    if (sceneData.seedData.staticCloudFile) {
      promises.unshift(
        parseFile(sceneData.seedData.staticCloudFile).catch((e) => {
          console.error(
            `Failed to load static cloud ${sceneData.seedData.staticCloudFile.name}:`,
            e
          );
          return null;
        })
      );
    }

    const results = await Promise.all(promises);

    let staticFrame = null;
    let frames = [];
    if (sceneData.seedData.staticCloudFile) {
      staticFrame = results.shift();
      frames = results.filter((f) => f !== null);
    } else {
      frames = results.filter((f) => f !== null);
    }

    return { frames, staticFrame };
  }

  // --- Color Generation ---

  getColorsForFrame(frameData, mode) {
    switch (mode) {
      case "rgb":
        return frameData.colors;
      case "normal":
        return this.generateNormalColors(frameData.normals);
      case "label":
      default:
        return this.generateLabelColors(frameData.labels);
    }
  }

  generateLabelColors(labels) {
    const c = new Float32Array(labels.length * 3);
    for (let i = 0; i < labels.length; i++) {
      c.set(this.getColorForLabel(labels[i]), i * 3);
    }
    return c;
  }

  generateNormalColors(normals) {
    const colors = new Float32Array(normals.length);
    for (let i = 0; i < normals.length; i++) {
      colors[i] = (normals[i] + 1) / 2; // Map [-1, 1] to [0, 1]
    }
    return colors;
  }

  getColorForLabel(label) {
    const l = String(label).toLowerCase();
    if (this.colorMap[label]) return this.colorMap[label];
    if (this.colorMap[l]) return this.colorMap[l];
    for (const k in this.colorMap) {
      if (l.includes(k.toLowerCase())) return this.colorMap[k];
    }
    // Fallback to a generated color based on a hash of the label
    let hash = 0;
    for (let i = 0; i < l.length; i++) {
      hash = l.charCodeAt(i) + ((hash << 5) - hash);
    }
    const color = new Array(3);
    for (let i = 0; i < 3; i++) {
      color[i] = ((hash >> (i * 8)) & 0xff) / 255;
    }
    return color;
  }

  createColorMap() {
    return {
      SMPL: [1.0, 0.2, 0.2],
      character: [1.0, 0.4, 0.0],
      person: [1.0, 0.0, 0.8],
      human: [0.8, 0.0, 1.0],
      Floor: [0.7, 0.7, 0.7],
      floor: [0.6, 0.6, 0.6],
      Ceiling: [0.9, 0.9, 0.9],
      ceiling: [0.8, 0.8, 0.8],
      wall: [0.5, 0.5, 0.7],
      Wall: [0.4, 0.4, 0.6],
      chair: [0.6, 0.4, 0.2],
      table: [0.8, 0.6, 0.3],
      box: [0.3, 0.6, 0.3],
      furniture: [0.5, 0.3, 0.1],
      default: [0.8, 0.8, 0.8],
      f_avg: [0, 0, 0],
      Scene: [0.8, 0.8, 0.8],
    };
  }
}
