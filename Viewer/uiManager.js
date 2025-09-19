// uiManager.js

export class UIManager {
  constructor() {
    this.dom = this.cacheDOMElements();
    this.eventListeners = {};
    this.bindEventListeners();
  }

  // --- Event Emitter Pattern ---
  on(eventName, listener) {
    if (!this.eventListeners[eventName]) {
      this.eventListeners[eventName] = [];
    }
    this.eventListeners[eventName].push(listener);
  }

  emit(eventName, ...args) {
    if (this.eventListeners[eventName]) {
      this.eventListeners[eventName].forEach((listener) => listener(...args));
    }
  }

  cacheDOMElements() {
    const ids = [
      "sidebar",
      "main-content",
      "viewer-container",
      "video-container",
      "canvas-container",
      "resizer",
      "ui-overlay",
      "folder-upload",
      "breadcrumb",
      "tree-container",
      "loading",
      "loading-text",
      "panel-toggles",
      "toggle-sidebar",
      "toggle-panels",
      "toggle-video",
      "toggle-fullscreen",
      "info-panel",
      "settings-panel",
      "controls-panel",
      "scene-info",
      "frame-info",
      "points-info",
      "objects-info",
      "fps-info",
      "reset-view",
      "point-size",
      "point-size-value",
      "speed-control",
      "speed-value",
      "fps-control",
      "fps-value",
      "toggle-color-mode",
      "toggle-wireframe",
      "toggle-focus-point",
      "play-pause",
      "play-icon",
      "play-text",
      "prev-frame",
      "next-frame",
      "timeline-container",
      "timeline",
      "timeline-progress",
      "timeline-handle",
      "current-time",
      "total-time",
      "video-player",
      "video-placeholder",
    ];
    const dom = {};
    ids.forEach((id) => (dom[id] = document.getElementById(id)));
    dom.colorSwatches = document.querySelectorAll(".color-swatch");
    return dom;
  }

  bindEventListeners() {
    // Folder upload
    this.dom["folder-upload"].addEventListener("change", (e) =>
      this.emit("folder-upload", e.target.files)
    );

    // Tree view (Event Delegation)
    this.dom["tree-container"].addEventListener("click", (e) => {
      const item = e.target.closest(".tree-item");
      if (!item) return;

      if (item.dataset.path) {
        this.emit("tree-node-select", JSON.parse(item.dataset.path));
      } else if (item.classList.contains("folder")) {
        item.classList.toggle("collapsed");
      }
    });

    // Layout controls
    this.dom["toggle-sidebar"].addEventListener("click", () =>
      this.toggleSidebar()
    );
    this.dom["toggle-panels"].addEventListener("click", () =>
      this.togglePanels()
    );
    this.dom["toggle-video"].addEventListener("click", () =>
      this.toggleVideo()
    );
    this.dom["toggle-fullscreen"].addEventListener("click", () =>
      this.toggleFullscreen()
    );
    this.dom["resizer"].addEventListener("mousedown", (e) =>
      this.initResizer(e)
    );

    // Settings
    this.dom["point-size"].addEventListener("input", (e) => {
      const size = parseFloat(e.target.value);
      this.dom["point-size-value"].textContent = size.toFixed(2);
      this.emit("point-size-change", size);
    });
    this.dom["speed-control"].addEventListener("input", (e) => {
      const speed = parseFloat(e.target.value);
      this.dom["speed-value"].textContent = speed.toFixed(1);
      this.emit("speed-change", speed);
    });
    this.dom["fps-control"].addEventListener("input", (e) => {
      const fps = parseInt(e.target.value, 10);
      this.dom["fps-value"].textContent = fps;
      this.emit("fps-change", fps);
    });

    this.dom["toggle-color-mode"].addEventListener("click", (e) => {
      const btn = e.currentTarget;
      let mode;
      if (btn.innerText.includes("Label")) {
        mode = "rgb";
        btn.innerHTML = "🎨 View: RGB";
      } else if (btn.innerText.includes("RGB")) {
        mode = "normal";
        btn.innerHTML = "🌐 View: Normal";
      } else {
        mode = "label";
        btn.innerHTML = "🎨 View: Label";
      }
      this.emit("color-mode-toggle", mode);
    });
    this.dom["toggle-wireframe"].addEventListener("click", (e) => {
      e.currentTarget.classList.toggle("active");
      this.emit(
        "wireframe-toggle",
        e.currentTarget.classList.contains("active")
      );
    });
    this.dom["toggle-focus-point"].addEventListener("click", (e) => {
      e.currentTarget.classList.toggle("active");
      this.emit(
        "focus-point-toggle",
        e.currentTarget.classList.contains("active")
      );
    });
    this.dom.colorSwatches.forEach((s) =>
      s.addEventListener("click", (e) => {
        this.dom.colorSwatches.forEach((sw) => sw.classList.remove("active"));
        e.currentTarget.classList.add("active");
        this.emit("bg-color-change", e.currentTarget.dataset.color);
      })
    );

    // Playback
    this.dom["play-pause"].addEventListener("click", () =>
      this.emit("play-pause")
    );
    this.dom["next-frame"].addEventListener("click", () =>
      this.emit("next-frame")
    );
    this.dom["prev-frame"].addEventListener("click", () =>
      this.emit("prev-frame")
    );
    this.dom["reset-view"].addEventListener("click", () =>
      this.emit("reset-view")
    );

    // Timeline Scrubbing
    this.bindTimelineEvents();

    // Keyboard shortcuts
    document.addEventListener("keydown", (e) => {
      if (e.target.tagName === "INPUT") return;
      const keyMap = {
        " ": "play-pause",
        arrowleft: "prev-frame",
        arrowright: "next-frame",
        r: "reset-view",
      };
      if (keyMap[e.key.toLowerCase()]) {
        e.preventDefault();
        this.emit(keyMap[e.key.toLowerCase()]);
      }
      if (e.key.toLowerCase() === "h") {
        e.preventDefault();
        this.togglePanels();
      }
      if (e.key.toLowerCase() === "s") {
        e.preventDefault();
        this.toggleSidebar();
      }
      if (e.key.toLowerCase() === "v") {
        e.preventDefault();
        this.toggleVideo();
      }
      if (e.key.toLowerCase() === "f") {
        e.preventDefault();
        this.toggleFullscreen();
      }
    });
  }

  bindTimelineEvents() {
    const timeline = this.dom.timeline;
    const seekFromEvent = (e) => {
      const rect = timeline.getBoundingClientRect();
      const progress = Math.max(
        0,
        Math.min(1, (e.clientX - rect.left) / rect.width)
      );
      const frameCount = parseInt(
        this.dom["frame-info"].textContent.split("/")[1].trim(),
        10
      );
      if (!isNaN(frameCount) && frameCount > 0) {
        this.emit("seek", Math.floor(progress * (frameCount - 1)));
      }
    };

    timeline.addEventListener("click", seekFromEvent);
    this.dom["timeline-handle"].addEventListener("mousedown", (e) => {
      e.preventDefault();
      const onMove = (moveEvent) => seekFromEvent(moveEvent);
      const onUp = () => {
        document.removeEventListener("mousemove", onMove);
        document.removeEventListener("mouseup", onUp);
      };
      document.addEventListener("mousemove", onMove);
      document.addEventListener("mouseup", onUp);
    });
  }

  // --- UI Update Methods ---

  renderTree(structure) {
    const container = this.dom["tree-container"];
    container.innerHTML = "";
    const fragment = document.createDocumentFragment();

    const createNode = (name, type, icon, path, data = {}) => {
      const node = document.createElement("div");
      node.className = "tree-node";
      const item = document.createElement("div");
      item.className = `tree-item ${type}`;
      item.innerHTML = `<span class="tree-icon">${icon}</span><span>${name}</span>`;

      if (type === "scene") {
        item.dataset.path = JSON.stringify(path);
        node.appendChild(item);
        if (data.description) {
          const desc = document.createElement("div");
          desc.className = "tree-item-description";
          desc.textContent = data.description;
          node.appendChild(desc);
        }
      } else {
        item.classList.add("folder");
        node.appendChild(item);
      }
      return node;
    };

    for (const [roomName, room] of Object.entries(structure)) {
      const roomNode = createNode(roomName, "folder", "📁", [roomName]);
      const roomChildren = document.createElement("div");
      roomChildren.className = "tree-children";
      for (const [sceneName, scene] of Object.entries(room)) {
        const sceneNode = createNode(sceneName, "folder", "🎬", [
          roomName,
          sceneName,
        ]);
        const sceneChildren = document.createElement("div");
        sceneChildren.className = "tree-children";
        for (const [seedName, seedData] of Object.entries(scene)) {
          const seedNode = createNode(seedName, "folder", "🌱", [
            roomName,
            sceneName,
            seedName,
          ]);
          const seedChildren = document.createElement("div");
          seedChildren.className = "tree-children";
          for (const [promptName, promptData] of Object.entries(
            seedData.prompts
          )) {
            const promptNode = createNode(
              `${promptName} (${promptData.pointCloudFiles.length} frames)`,
              "scene",
              "🎥",
              [roomName, sceneName, seedName, promptName],
              promptData
            );
            seedChildren.appendChild(promptNode);
          }
          seedNode.appendChild(seedChildren);
          sceneChildren.appendChild(seedNode);
        }
        sceneNode.appendChild(sceneChildren);
        roomChildren.appendChild(sceneNode);
      }
      roomNode.appendChild(roomChildren);
      fragment.appendChild(roomNode);
    }
    container.appendChild(fragment);
  }

  updateTreeDescription(path, description) {
    if (!description) return;
    const pathStr = JSON.stringify(path);
    const item = this.dom["tree-container"].querySelector(
      `.tree-item[data-path='${pathStr}']`
    );
    if (item) {
      let descEl = item.nextElementSibling;
      if (!descEl || !descEl.classList.contains("tree-item-description")) {
        descEl = document.createElement("div");
        descEl.className = "tree-item-description";
        item.insertAdjacentElement("afterend", descEl);
      }
      descEl.textContent = description;
    }
  }

  toggleLoading(show, text = "Loading...") {
    this.dom.loading.classList.toggle("hidden", !show);
    this.dom["loading-text"].textContent = text;
  }

  updateBreadcrumb(text, isError = false) {
    this.dom.breadcrumb.innerHTML = `<span>${text}</span>`;
    this.dom.breadcrumb.style.color = isError ? "#ff4d4f" : "";
  }

  updateSelectedTreeItem(path) {
    const pathStr = JSON.stringify(path);
    this.dom["tree-container"]
      .querySelectorAll(".tree-item.selected")
      .forEach((el) => el.classList.remove("selected"));
    const item = this.dom["tree-container"].querySelector(
      `.tree-item[data-path='${pathStr}']`
    );
    if (item) item.classList.add("selected");
  }

  updateSceneInfo(name) {
    this.dom["scene-info"].textContent = name;
  }
  updateFrameInfo(current, total) {
    this.dom["frame-info"].textContent = `${current} / ${total}`;
  }
  updatePointsInfo(count) {
    this.dom["points-info"].textContent = count.toLocaleString();
  }
  updateObjectsInfo(count) {
    this.dom["objects-info"].textContent = count;
  }
  updateFps(fps) {
    this.dom["fps-info"].textContent = fps;
  }

  updateTimeline(progress) {
    // progress is 0 to 1
    const percent = (progress * 100).toFixed(2);
    this.dom["timeline-progress"].style.width = `${percent}%`;
    this.dom["timeline-handle"].style.left = `${percent}%`;
  }

  updateTimeLabels(current, total) {
    const format = (s) =>
      `${String(Math.floor(s / 60)).padStart(2, "0")}:${String(
        Math.floor(s % 60)
      ).padStart(2, "0")}`;
    this.dom["current-time"].textContent = format(current);
    this.dom["total-time"].textContent = format(total);
  }

  updatePlayButton(isPlaying) {
    this.dom["play-icon"].textContent = isPlaying ? "⏸️" : "▶️";
    this.dom["play-text"].textContent = isPlaying ? "Pause" : "Play";
  }

  loadVideo(videoFile) {
    const player = this.dom["video-player"];
    const placeholder = this.dom["video-placeholder"];
    if (player.src) URL.revokeObjectURL(player.src);

    if (videoFile) {
      player.src = URL.createObjectURL(videoFile);
      player.classList.remove("hidden");
      placeholder.classList.add("hidden");
      player.load();
    } else {
      player.classList.add("hidden");
      placeholder.classList.remove("hidden");
      placeholder.textContent = "No video available for this selection";
    }
  }

  syncVideoToFrame(frame, fps, play = false) {
    const player = this.dom["video-player"];
    if (player.src && player.readyState > 0) {
      player.currentTime = frame / fps;
      play
        ? player.play().catch((e) => console.warn("Video play failed", e))
        : player.pause();
    }
  }

  // --- Layout Toggles ---
  toggleSidebar() {
    this.dom.sidebar.classList.toggle("hidden");
    this.dom["toggle-sidebar"].classList.toggle("active");
  }
  togglePanels() {
    this.dom["ui-overlay"].classList.toggle("panels-hidden");
    this.dom["toggle-panels"].classList.toggle("active");
  }
  toggleVideo() {
    const isHidden = this.dom["video-container"].classList.toggle("hidden");
    this.dom["toggle-video"].classList.toggle("active");
    this.dom.resizer.style.display = isHidden ? "none" : "block";
    setTimeout(() => this.emit("resize-view"), 300);
  }
  toggleFullscreen() {
    if (!document.fullscreenElement) {
      document.documentElement.requestFullscreen();
    } else {
      document.exitFullscreen();
    }
  }
  initResizer(e) {
    e.preventDefault();
    const vCont = this.dom["video-container"];
    let h = vCont.offsetHeight,
      y = e.clientY;
    const onMove = (me) => {
      let newH = h - (me.clientY - y);
      newH = Math.max(50, newH);
      vCont.style.height = `${newH}px`;
      this.emit("resize-view");
    };
    const onUp = () => {
      document.removeEventListener("mousemove", onMove);
      document.removeEventListener("mouseup", onUp);
    };
    document.addEventListener("mousemove", onMove);
    document.addEventListener("mouseup", onUp);
  }
}
