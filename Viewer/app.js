import { DataManager } from "./dataManager.js";
import { Viewer3D } from "./viewer3D.js";
import { UIManager } from "./uiManager.js";

class App {
  constructor() {
    this.dataManager = new DataManager();
    this.viewer = new Viewer3D(document.getElementById("canvas-container"));
    this.ui = new UIManager();

    this.frames = [];
    this.staticFrameData = null;
    this.currentSceneData = null;

    this.state = {
      currentFrame: 0,
      isPlaying: false,
      fps: 10,
      speed: 1.0,
      colorMode: "label", // 'label', 'rgb', or 'normal'
    };

    this.frameInterval = null;
    this.lastRenderTime = 0;

    this.bindEventListeners();
    this.animate();
  }

  bindEventListeners() {
    // Data related events
    this.ui.on("folder-upload", (files) => this.loadDataset(files));
    this.ui.on("tree-node-select", (path) => this.loadScene(path));

    // Playback controls
    this.ui.on("play-pause", () => this.togglePlayback());
    this.ui.on("next-frame", () => this.nextFrame());
    this.ui.on("prev-frame", () => this.previousFrame());
    this.ui.on("seek", (frame) => this.seekToFrame(frame));
    this.ui.on("fps-change", (fps) => {
      this.state.fps = fps;
      if (this.state.isPlaying) this.startPlayback();
      this.updateUITimeLabels();
    });
    this.ui.on("speed-change", (speed) => {
      this.state.speed = speed;
      if (this.state.isPlaying) this.startPlayback();
    });

    // Viewer settings
    this.ui.on("point-size-change", (size) =>
      this.viewer.updatePointSize(size)
    );
    this.ui.on("reset-view", () => this.viewer.resetView());
    this.ui.on("color-mode-toggle", (mode) => {
      this.state.colorMode = mode;
      this.updatePointColors();
    });
    this.ui.on("wireframe-toggle", (show) => this.viewer.toggleWireframe(show));
    this.ui.on("focus-point-toggle", (show) =>
      this.viewer.toggleFocusPoint(show)
    );
    this.ui.on("bg-color-change", (color) =>
      this.viewer.setBackgroundColor(color)
    );

    // Window/Layout events
    window.addEventListener("resize", () => this.viewer.updateCameraAspect());
    this.ui.on("resize-view", () => this.viewer.updateCameraAspect());
  }

  async loadDataset(files) {
    if (!files || files.length === 0) return;
    this.ui.toggleLoading(true, "Building dataset structure...");
    try {
      const structure = await this.dataManager.buildDatasetStructure(files);
      this.ui.renderTree(structure);
      this.ui.updateBreadcrumb("Dataset loaded - select a scene");
    } catch (error) {
      console.error("Error loading dataset:", error);
      this.ui.updateBreadcrumb("Error loading dataset.", true);
    } finally {
      this.ui.toggleLoading(false);
    }
  }

  async loadScene(path) {
    this.stopPlayback();
    this.ui.toggleLoading(true, `Loading scene: ${path[path.length - 1]}`);

    this.currentSceneData = this.dataManager.getSceneData(path);

    // Lazy load description
    await this.dataManager.loadDescriptionForScene(path);
    this.ui.updateTreeDescription(
      path,
      this.currentSceneData.prompt.description
    );

    // Load point cloud data via worker
    const { frames, staticFrame } = await this.dataManager.loadPointCloudData(
      this.currentSceneData
    );
    this.frames = frames;
    this.staticFrameData = staticFrame;

    // Update 3D viewer
    this.viewer.clearScene();
    if (this.staticFrameData) {
      this.viewer.addStaticPoints(this.staticFrameData);
    }

    this.state.currentFrame = 0;
    if (this.frames.length > 0) {
      this.seekToFrame(0);
    } else {
      this.updateUI();
    }
    this.updatePointColors();
    this.viewer.resetView();

    // Update UI
    this.ui.updateSelectedTreeItem(path);
    this.ui.updateBreadcrumb(path.join(" → "));
    this.ui.loadVideo(this.currentSceneData.prompt.videoFile);
    this.ui.toggleLoading(false);
  }

  updatePointColors() {
    const currentFrame = this.frames[this.state.currentFrame];
    if (currentFrame) {
      const colors = this.dataManager.getColorsForFrame(
        currentFrame,
        this.state.colorMode
      );
      this.viewer.updateDynamicPointColors(colors);
    }
    if (this.staticFrameData) {
      const staticColors = this.dataManager.getColorsForFrame(
        this.staticFrameData,
        this.state.colorMode
      );
      this.viewer.updateStaticPointColors(staticColors);
    }
  }

  // --- Playback Logic ---

  seekToFrame(frameIndex) {
    if (this.frames.length === 0) return;
    const newFrame = Math.max(0, Math.min(this.frames.length - 1, frameIndex));

    if (this.frames[newFrame]) {
      this.state.currentFrame = newFrame;
      this.viewer.displayFrame(this.frames[this.state.currentFrame]);
      this.updatePointColors();
      this.ui.syncVideoToFrame(this.state.currentFrame, this.state.fps);
      this.updateUI();
    }
  }

  togglePlayback() {
    if (this.frames.length === 0) return;
    this.state.isPlaying = !this.state.isPlaying;
    if (this.state.isPlaying) {
      this.startPlayback();
      this.ui.syncVideoToFrame(this.state.currentFrame, this.state.fps, true);
    } else {
      this.stopPlayback();
      this.ui.syncVideoToFrame(this.state.currentFrame, this.state.fps, false);
    }
    this.ui.updatePlayButton(this.state.isPlaying);
  }

  startPlayback() {
    if (this.frameInterval) clearInterval(this.frameInterval);
    this.frameInterval = setInterval(() => {
      let nextFrame = this.state.currentFrame + 1;
      if (nextFrame >= this.frames.length) {
        nextFrame = 0; // Loop
        this.ui.syncVideoToFrame(0, this.state.fps, true);
      }
      this.seekToFrame(nextFrame);
    }, 1000 / this.state.fps / this.state.speed);
  }

  stopPlayback() {
    clearInterval(this.frameInterval);
    this.frameInterval = null;
  }

  nextFrame() {
    this.seekToFrame(this.state.currentFrame + 1);
  }

  previousFrame() {
    this.seekToFrame(this.state.currentFrame - 1);
  }

  // --- UI Update Logic ---

  updateUI() {
    const frameCount = this.frames.length;
    const currentFrameNumber = frameCount > 0 ? this.state.currentFrame + 1 : 0;
    const totalPoints =
      (this.staticFrameData?.pointCount || 0) +
      (this.frames[this.state.currentFrame]?.pointCount || 0);

    if (this.currentSceneData)
      this.ui.updateSceneInfo(this.currentSceneData.promptName);
    this.ui.updateFrameInfo(currentFrameNumber, frameCount);
    this.ui.updatePointsInfo(totalPoints);

    const objects = this.viewer.getVisibleObjectCount();
    this.ui.updateObjectsInfo(objects);

    const progress =
      frameCount > 1 ? this.state.currentFrame / (frameCount - 1) : 0;
    this.ui.updateTimeline(progress);
    this.updateUITimeLabels();
  }

  updateUITimeLabels() {
    const frameCount = this.frames.length;
    const currentTime =
      frameCount > 0 ? this.state.currentFrame / this.state.fps : 0;
    const totalTime = frameCount > 0 ? (frameCount - 1) / this.state.fps : 0;
    this.ui.updateTimeLabels(currentTime, totalTime);
  }

  // --- Render Loop ---

  animate() {
    requestAnimationFrame(() => this.animate());
    const now = performance.now();
    const delta = now - this.lastRenderTime;
    if (delta > 0) {
      const fps = 1000 / delta;
      this.ui.updateFps(Math.round(fps));
    }
    this.lastRenderTime = now;
    this.viewer.render();
  }
}

document.addEventListener("DOMContentLoaded", () => {
  new App();
});
