// viewer3D.js

const MAX_POINTS = 4_000_000; // Pre-allocate buffer for ~4 million points

export class Viewer3D {
  constructor(container) {
    this.container = container;
    this.scene = new THREE.Scene();
    this.camera = new THREE.PerspectiveCamera(
      75,
      container.clientWidth / container.clientHeight,
      0.1,
      1000
    );
    this.renderer = new THREE.WebGLRenderer({ antialias: true, alpha: true });

    this.raycaster = new THREE.Raycaster();
    this.mouse = new THREE.Vector2();

    this.points = null;
    this.staticPoints = null;
    this.wireframeGroup = new THREE.Group();

    this.cameraTarget = new THREE.Vector3(0, 0, 0);
    this.targetIndicator = null;

    this.isCKeyDown = false;

    this.init();
    this.initControls();
  }

  init() {
    this.renderer.setSize(
      this.container.clientWidth,
      this.container.clientHeight
    );
    this.renderer.setClearColor(0x1a1a1a, 1);
    this.container.appendChild(this.renderer.domElement);

    this.camera.position.set(5, 5, 5);
    this.camera.lookAt(this.cameraTarget);
    this.scene.background = new THREE.Color(0x1a1a1a);

    this.scene.add(new THREE.AmbientLight(0x404040, 2.0));
    this.scene.add(new THREE.DirectionalLight(0xffffff, 1.5));
    this.scene.add(new THREE.AxesHelper(2));

    const indicatorGeom = new THREE.SphereGeometry(0.05, 16, 16);
    const indicatorMat = new THREE.MeshBasicMaterial({
      color: 0x00ffff,
      wireframe: true,
    });
    this.targetIndicator = new THREE.Mesh(indicatorGeom, indicatorMat);
    this.scene.add(this.targetIndicator);

    this.scene.add(this.wireframeGroup);
    this.wireframeGroup.visible = false;

    // --- OPTIMIZATION: Create Points object once with a large buffer ---
    const createPointsObject = () => {
      const geom = new THREE.BufferGeometry();
      geom.setAttribute(
        "position",
        new THREE.BufferAttribute(new Float32Array(MAX_POINTS * 3), 3).setUsage(
          THREE.DynamicDrawUsage
        )
      );
      geom.setAttribute(
        "color",
        new THREE.BufferAttribute(new Float32Array(MAX_POINTS * 3), 3).setUsage(
          THREE.DynamicDrawUsage
        )
      );
      const mat = new THREE.PointsMaterial({
        size: 0.3,
        vertexColors: true,
        sizeAttenuation: true,
      });
      const points = new THREE.Points(geom, mat);
      points.frustumCulled = false; // Important for large scenes
      return points;
    };

    this.points = createPointsObject();
    this.staticPoints = createPointsObject();
    this.scene.add(this.points, this.staticPoints);
  }

  initControls() {
    // ... (your extensive camera control logic would go here)
    // This is a simplified version of your original code
    const canvas = this.renderer.domElement;
    let isMouseDown = false,
      isPanning = false;
    const prevMouse = new THREE.Vector2();

    canvas.addEventListener("contextmenu", (e) => e.preventDefault());
    canvas.addEventListener("mousedown", (e) => {
      isMouseDown = true;
      isPanning = e.button === 2;
      prevMouse.set(e.clientX, e.clientY);
    });
    canvas.addEventListener("mouseup", () => {
      isMouseDown = false;
      isPanning = false;
    });
    canvas.addEventListener("mousemove", (e) => {
      if (!isMouseDown) return;
      const delta = new THREE.Vector2(
        e.clientX - prevMouse.x,
        e.clientY - prevMouse.y
      );

      if (isPanning) {
        const dist = this.camera.position.distanceTo(this.cameraTarget);
        const panSpeed = dist * 0.002;
        const right = new THREE.Vector3()
          .setFromMatrixColumn(this.camera.matrix, 0)
          .multiplyScalar(-delta.x * panSpeed);
        const up = new THREE.Vector3()
          .setFromMatrixColumn(this.camera.matrix, 1)
          .multiplyScalar(delta.y * panSpeed);
        this.camera.position.add(right).add(up);
        this.cameraTarget.add(right).add(up);
      } else {
        const offset = this.camera.position.clone().sub(this.cameraTarget);
        const spherical = new THREE.Spherical().setFromVector3(offset);
        spherical.theta -= delta.x * 0.005;
        spherical.phi -= delta.y * 0.005;
        spherical.phi = Math.max(0.01, Math.min(Math.PI - 0.01, spherical.phi));
        offset.setFromSpherical(spherical);
        this.camera.position.copy(this.cameraTarget).add(offset);
      }
      this.camera.lookAt(this.cameraTarget);
      this.targetIndicator.position.copy(this.cameraTarget);
      prevMouse.set(e.clientX, e.clientY);
    });
    canvas.addEventListener("wheel", (e) => {
      e.preventDefault();
      const scale = e.deltaY > 0 ? 1.1 : 0.9;
      this.camera.position
        .sub(this.cameraTarget)
        .multiplyScalar(scale)
        .add(this.cameraTarget);
    });

    window.addEventListener("keydown", (e) => {
      if (e.key.toLowerCase() === "c") this.isCKeyDown = true;
    });
    window.addEventListener("keyup", (e) => {
      if (e.key.toLowerCase() === "c") this.isCKeyDown = false;
    });
    canvas.addEventListener("click", (e) => {
      if (!this.isCKeyDown) return;
      const rect = canvas.getBoundingClientRect();
      this.mouse.set(
        ((e.clientX - rect.left) / rect.width) * 2 - 1,
        -((e.clientY - rect.top) / rect.height) * 2 + 1
      );
      this.raycaster.setFromCamera(this.mouse, this.camera);
      const intersects = this.raycaster.intersectObjects(
        [this.points, this.staticPoints],
        false
      );
      if (intersects.length > 0) {
        this.cameraTarget.copy(intersects[0].point);
        this.targetIndicator.position.copy(this.cameraTarget);
        this.camera.lookAt(this.cameraTarget);
      }
    });
  }

  clearScene() {
    this.points.geometry.setDrawRange(0, 0);
    this.staticPoints.geometry.setDrawRange(0, 0);
    while (this.wireframeGroup.children.length > 0)
      this.wireframeGroup.remove(this.wireframeGroup.children[0]);
  }

  addStaticPoints(frameData) {
    if (!frameData || frameData.pointCount === 0) return;
    this.staticPoints.geometry.attributes.position.array.set(
      frameData.positions
    );
    this.staticPoints.geometry.attributes.position.needsUpdate = true;
    this.staticPoints.geometry.setDrawRange(0, frameData.pointCount);
  }

  displayFrame(frameData) {
    if (!frameData) return;
    // --- OPTIMIZATION: Update buffer data in-place ---
    const geom = this.points.geometry;
    geom.attributes.position.array.set(frameData.positions);
    geom.attributes.position.needsUpdate = true;
    geom.setDrawRange(0, frameData.pointCount);

    this.createWireframesForFrame(frameData);
  }

  createWireframesForFrame(frameData) {
    // Clear old wireframes
    while (this.wireframeGroup.children.length > 0) {
      const child = this.wireframeGroup.children[0];
      this.wireframeGroup.remove(child);
      child.geometry.dispose();
      child.material.dispose();
    }

    const pointsByLabel = new Map();
    for (let i = 0; i < frameData.pointCount; i++) {
      const label = frameData.labels[i];
      const lowerLabel = String(label).toLowerCase();
      if (
        ["default", "floor", "wall", "ceiling", "scene"].some((term) =>
          lowerLabel.includes(term)
        )
      )
        continue;

      if (!pointsByLabel.has(label)) pointsByLabel.set(label, []);
      pointsByLabel
        .get(label)
        .push(new THREE.Vector3().fromArray(frameData.positions, i * 3));
    }

    for (const [label, points] of pointsByLabel.entries()) {
      if (points.length < 2) continue;
      const box = new THREE.Box3().setFromPoints(points);
      if (box.isEmpty()) continue;
      // Note: color here is hardcoded, should be passed from dataManager ideally
      this.wireframeGroup.add(
        new THREE.Box3Helper(box, new THREE.Color(0xff00ff))
      );
    }
  }

  getVisibleObjectCount() {
    return this.wireframeGroup.children.length;
  }

  // --- Update Methods ---
  updatePointSize(size) {
    if (this.points) this.points.material.size = size;
    if (this.staticPoints) this.staticPoints.material.size = size;
  }

  updateDynamicPointColors(colors) {
    if (!this.points || !colors) return;
    this.points.geometry.attributes.color.array.set(colors);
    this.points.geometry.attributes.color.needsUpdate = true;
  }

  updateStaticPointColors(colors) {
    if (!this.staticPoints || !colors) return;
    this.staticPoints.geometry.attributes.color.array.set(colors);
    this.staticPoints.geometry.attributes.color.needsUpdate = true;
  }

  setBackgroundColor(color) {
    const bgColor = new THREE.Color(parseInt(color));
    this.scene.background = bgColor;
    this.renderer.setClearColor(bgColor, 1);
  }

  toggleWireframe(show) {
    this.wireframeGroup.visible = show;
  }
  toggleFocusPoint(show) {
    this.targetIndicator.visible = show;
  }

  updateCameraAspect() {
    const rect = this.container.getBoundingClientRect();
    this.camera.aspect = rect.width / rect.height;
    this.camera.updateProjectionMatrix();
    this.renderer.setSize(rect.width, rect.height);
  }

  resetView() {
    const box = new THREE.Box3();
    if (this.staticPoints.geometry.drawRange.count > 0) {
      this.staticPoints.geometry.computeBoundingBox();
      box.union(this.staticPoints.geometry.boundingBox);
    }
    if (this.points.geometry.drawRange.count > 0) {
      this.points.geometry.computeBoundingBox();
      box.union(this.points.geometry.boundingBox);
    }

    if (!box.isEmpty()) {
      box.getCenter(this.cameraTarget);
      const size = box.getSize(new THREE.Vector3());
      const distance = Math.max(size.x, size.y, size.z) * 1.5;
      this.camera.position
        .copy(this.cameraTarget)
        .add(new THREE.Vector3(0, distance * 0.5, distance));
    } else {
      this.cameraTarget.set(0, 0, 0);
      this.camera.position.set(5, 5, 5);
    }

    this.targetIndicator.position.copy(this.cameraTarget);
    this.camera.lookAt(this.cameraTarget);
  }

  render() {
    this.renderer.render(this.scene, this.camera);
  }
}
