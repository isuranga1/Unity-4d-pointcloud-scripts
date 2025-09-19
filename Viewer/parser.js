// parser.js

self.onmessage = async (event) => {
  const { id, file } = event.data;
  try {
    const content = await file.text();
    const ext = file.name.split(".").pop().toLowerCase();
    let frameData;
    if (ext === "pcd") {
      frameData = parsePCD(content);
    } else if (["ply", "xyz", "txt"].includes(ext)) {
      frameData = parseGeneric(content, ext);
    } else {
      throw new Error(`Unsupported file format: ${ext}`);
    }

    // Send back the data, transferring buffer ownership for zero-copy
    self.postMessage(
      {
        id: id,
        success: true,
        payload: frameData,
      },
      [
        frameData.positions.buffer,
        frameData.colors.buffer,
        frameData.normals.buffer,
      ]
    );
  } catch (error) {
    self.postMessage({ id: id, success: false, error: error.message });
  }
};

function parseGeneric(content, ext) {
  const lines = content.split("\n");
  let dataStart = 0;
  if (ext === "ply") {
    dataStart = lines.findIndex((l) => l.trim() === "end_header") + 1;
    if (dataStart === 0)
      dataStart = lines.findIndex((l) => l.trim().startsWith("element vertex")); // Simple fallback
  }

  const positions = [],
    labels = [];
  for (let i = dataStart; i < lines.length; i++) {
    const parts = lines[i].trim().split(/\s+/);
    if (parts.length >= 3) {
      const [x, y, z] = parts.slice(0, 3).map(parseFloat);
      if (!isNaN(x)) {
        positions.push(x, y, z);
        labels.push(parts.length > 6 ? parts[6] : "default");
      }
    }
  }
  return _createDefaultFrameData(positions, labels);
}

function _createDefaultFrameData(positions, labels) {
  const numPoints = positions.length / 3;
  const colors = new Float32Array(numPoints * 3);
  colors.fill(1.0); // Default to white
  const normals = new Float32Array(numPoints * 3);
  for (let i = 0; i < numPoints; i++) normals.set([0, 1, 0], i * 3); // Default up

  return {
    positions: new Float32Array(positions),
    labels,
    colors,
    normals,
    pointCount: numPoints,
  };
}

function parsePCD(content) {
  const lines = content.split("\n");
  let dataStart = 0,
    pointCount = 0;
  let fields = [];
  for (let i = 0; i < lines.length; i++) {
    const line = lines[i].trim();
    if (line.startsWith("FIELDS"))
      fields = line
        .split(" ")
        .slice(1)
        .map((n) => n.toLowerCase());
    else if (line.startsWith("POINTS"))
      pointCount = parseInt(line.split(" ")[1]);
    else if (line.startsWith("DATA ascii")) {
      dataStart = i + 1;
      break;
    } else if (line.startsWith("DATA"))
      throw new Error("Only ASCII PCD format is supported");
  }

  const positions = new Float32Array(pointCount * 3);
  const labels = new Array(pointCount);
  const colors = new Float32Array(pointCount * 3);
  const normals = new Float32Array(pointCount * 3);

  const idx = {
    x: fields.indexOf("x"),
    y: fields.indexOf("y"),
    z: fields.indexOf("z"),
    rgb: fields.indexOf("rgb"),
    label: fields.includes("label")
      ? fields.indexOf("label")
      : fields.indexOf("semantic"),
    nx: fields.indexOf("normal_x"),
    ny: fields.indexOf("normal_y"),
    nz: fields.indexOf("normal_z"),
  };

  let validPointIndex = 0;
  for (let i = dataStart; i < dataStart + pointCount && i < lines.length; i++) {
    const parts = lines[i].trim().split(/\s+/);
    const x = parseFloat(parts[idx.x]);
    if (isNaN(x) || parts.length < fields.length) continue;

    const pIdx = validPointIndex * 3;
    positions[pIdx] = x;
    positions[pIdx + 1] = parseFloat(parts[idx.y]);
    positions[pIdx + 2] = parseFloat(parts[idx.z]);

    labels[validPointIndex] =
      idx.label !== -1 && parts[idx.label] ? parts[idx.label] : "default";

    if (idx.rgb !== -1) {
      const packed = parseInt(parts[idx.rgb]);
      colors[pIdx] = ((packed >> 16) & 0xff) / 255;
      colors[pIdx + 1] = ((packed >> 8) & 0xff) / 255;
      colors[pIdx + 2] = (packed & 0xff) / 255;
    } else {
      colors.fill(1.0, pIdx, pIdx + 3);
    }

    if (idx.nx !== -1) {
      normals[pIdx] = parseFloat(parts[idx.nx]);
      normals[pIdx + 1] = parseFloat(parts[idx.ny]);
      normals[pIdx + 2] = parseFloat(parts[idx.nz]);
    } else {
      normals[pIdx] = 0.0;
      normals[pIdx + 1] = 1.0;
      normals[pIdx + 2] = 0.0;
    }
    validPointIndex++;
  }

  // Trim arrays if there were invalid points
  return {
    positions: positions.slice(0, validPointIndex * 3),
    labels: labels.slice(0, validPointIndex),
    colors: colors.slice(0, validPointIndex * 3),
    normals: normals.slice(0, validPointIndex * 3),
    pointCount: validPointIndex,
  };
}
