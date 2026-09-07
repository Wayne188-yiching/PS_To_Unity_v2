import gsap from "gsap";
import ScrollTrigger from "gsap/ScrollTrigger";
import "./styles.css";

gsap.registerPlugin(ScrollTrigger);

const prefersReducedMotion = window.matchMedia("(prefers-reduced-motion: reduce)");
const motionStorageKey = "ps-to-unity-report-motion";
const chapters = [...document.querySelectorAll(".chapter")];
const railLinks = [...document.querySelectorAll(".chapter-rail a")];
const currentIndex = document.querySelector("#currentIndex");
const currentLabel = document.querySelector("#currentLabel");
const progressBar = document.querySelector("#globalProgressBar");
const motionToggle = document.querySelector("#motionToggle");
const motionModeLabel = document.querySelector("#motionModeLabel");

const readMotionPreference = () => {
  try {
    return localStorage.getItem(motionStorageKey) || "system";
  } catch {
    return "system";
  }
};

let motionPreference = readMotionPreference();

const useReducedMotion = () => (
  window.innerWidth <= 900
  || motionPreference === "reduced"
  || (motionPreference === "system" && prefersReducedMotion.matches)
);

const updateMotionToggle = () => {
  if (!motionToggle || !motionModeLabel) return;
  const reduced = useReducedMotion();
  motionModeLabel.textContent = reduced ? "REDUCED" : "FULL";
  motionToggle.dataset.mode = reduced ? "reduced" : "full";
  motionToggle.setAttribute("aria-pressed", String(!reduced));
  motionToggle.title = reduced
    ? "目前為靜態閱讀模式；點擊啟用完整 ScrollTrigger 動畫"
    : "目前為完整 ScrollTrigger 動畫；點擊切換靜態閱讀模式";
};

motionToggle?.addEventListener("click", () => {
  motionPreference = useReducedMotion() ? "full" : "reduced";
  try {
    localStorage.setItem(motionStorageKey, motionPreference);
  } catch {
    // file:// privacy settings can disable storage; the page still works for this session.
  }
  window.location.reload();
});

// stroke-dasharray is resolved in the space the stroke is generated in. These paths set
// vector-effect: non-scaling-stroke inside SVGs stretched by preserveAspectRatio="none",
// so that space is screen pixels rather than viewBox units. getTotalLength() reports
// viewBox units, which makes the dash shorter than the line it has to cover: the pattern
// repeats, so a lit segment shows near the right edge before the scroll even starts and
// the trace never reaches the last node.
const getRenderedLength = (path, samples = 240) => {
  const userLength = path.getTotalLength();
  const svg = path.ownerSVGElement;
  const matrix = path.getScreenCTM();
  if (!svg || !matrix) return userLength;
  const point = svg.createSVGPoint();
  let total = 0;
  let previous = null;
  for (let index = 0; index <= samples; index += 1) {
    const at = path.getPointAtLength((userLength * index) / samples);
    point.x = at.x;
    point.y = at.y;
    const screen = point.matrixTransform(matrix);
    if (previous) total += Math.hypot(screen.x - previous.x, screen.y - previous.y);
    previous = screen;
  }
  return total || userLength;
};

// Where a node sits along the path, as a fraction of its length. The flow path snakes
// through two rows joined by a long connector, so nodes are nowhere near evenly spaced
// along it: revealing them on an even cadence let the line run up to 15% of the path
// ahead of the node it was supposed to be arriving at. Node geometry and the path share
// the SVG user space and the viewBox scales uniformly, so this is viewport-independent.
const fractionAlongPath = (path, node, samples = 1200) => {
  const total = path.getTotalLength();
  const matrix = path.getScreenCTM();
  const rect = node.getBoundingClientRect();
  if (!matrix || !rect.width) return 0;
  const point = path.ownerSVGElement.createSVGPoint();
  const centreX = rect.left + rect.width / 2;
  const centreY = rect.top + rect.height / 2;
  let best = { distance: Infinity, fraction: 0 };
  for (let index = 0; index <= samples; index += 1) {
    const at = path.getPointAtLength((total * index) / samples);
    point.x = at.x;
    point.y = at.y;
    const screen = point.matrixTransform(matrix);
    const distance = Math.hypot(screen.x - centreX, screen.y - centreY);
    if (distance < best.distance) best = { distance, fraction: index / samples };
  }
  return best.fraction;
};

// Called again from each trace tween's function-based start value, so ScrollTrigger's
// invalidateOnRefresh re-measures after a resize instead of keeping a stale dash unit.
const setPathReady = (path) => {
  if (!path) return 0;
  const length = getRenderedLength(path);
  // Gap is twice the dash so the pattern can never repeat inside the line, even when a
  // scene scales its container mid-scroll and the rendered length drifts from the measured one.
  gsap.set(path, { strokeDasharray: `${length} ${length * 2}`, strokeDashoffset: length });
  return length;
};

const activateChapter = (chapter) => {
  const index = chapter.dataset.index;
  currentIndex.textContent = index;
  currentLabel.textContent = chapter.dataset.label;
  railLinks.forEach((link) => {
    const isCurrent = link.getAttribute("href") === `#${chapter.id}`;
    if (isCurrent) link.setAttribute("aria-current", "true");
    else link.removeAttribute("aria-current");
  });
};

const buildPreviousScene = () => {
  const chapter = document.querySelector("#previous");
  const scene = chapter.querySelector(".scene");
  const nodes = [...chapter.querySelectorAll("[data-trace-node]")];
  const path = chapter.querySelector("#previousTrace");
  const quote = chapter.querySelector("blockquote");
  const cue = chapter.querySelector(".scroll-cue");
  setPathReady(path);
  gsap.set(nodes, { opacity: 0.5, scale: 0.97, transformOrigin: "center" });
  gsap.set([quote, cue], { opacity: 0.58 });

  const timeline = gsap.timeline({
    defaults: { duration: 0.55, ease: "expo.out" },
    scrollTrigger: {
      trigger: chapter,
      start: "top top",
      end: "+=1500",
      pin: scene,
      scrub: 0.65,
      anticipatePin: 1,
      invalidateOnRefresh: true,
      id: "previous-state",
    },
  });

  timeline
    .to(nodes[0], { opacity: 1, scale: 1 }, 0)
    .fromTo(
      path,
      { strokeDashoffset: () => setPathReady(path) },
      { strokeDashoffset: 0, duration: 4, ease: "none" },
      0.25
    )
    .to(quote, { opacity: 1, duration: 0.7 }, 3.65)
    .to(cue, { opacity: 1, duration: 0.5 }, 4.05);

  nodes.slice(1).forEach((node, index) => {
    timeline.to(node, { opacity: 1, scale: 1 }, 0.75 + index * 0.92);
  });
};

const buildLimitationScene = () => {
  const chapter = document.querySelector("#limitation");
  const scene = chapter.querySelector(".scene");
  const items = [...chapter.querySelectorAll("[data-ambiguity]")];
  const statement = chapter.querySelector(".limitation-statement");
  const anchor = chapter.querySelector(".ambiguity-anchor");
  const board = chapter.querySelector(".ambiguity-core");

  gsap.set(items, { opacity: 0.2, x: 8 });
  gsap.set(statement, { opacity: 0.22 });
  gsap.set(anchor, { opacity: 0, scale: 0.7, transformOrigin: "center" });
  gsap.set(board, { clipPath: "inset(0 100% 0 0)", opacity: 0.6 });

  const timeline = gsap.timeline({
    defaults: { duration: 0.65, ease: "expo.out" },
    scrollTrigger: {
      trigger: chapter,
      start: "top top",
      end: "+=2300",
      pin: scene,
      scrub: 0.7,
      anticipatePin: 1,
      id: "limitation",
    },
  });

  timeline
    .to(board, { clipPath: "inset(0 0% 0 0)", opacity: 1, duration: 0.9 }, 0)
    .to(anchor, { opacity: 1, scale: 1 }, 0.45);
  items.forEach((item, index) => {
    timeline.to(item, { opacity: 1, x: 0 }, 0.55 + index * 0.78);
  });
  timeline.to(statement, { opacity: 1, duration: 0.8 }, 4.7);
};

const buildAgentLayerScene = () => {
  const chapter = document.querySelector("#agent-layer");
  const scene = chapter.querySelector(".scene");
  const agentLayer = chapter.querySelector("#agentReasoningLayer");
  const connectorLines = [...chapter.querySelectorAll(".stack-connector span")];
  const agentCapabilities = [...agentLayer.querySelectorAll(".stack-capabilities span")];
  const rules = [...chapter.querySelectorAll(".responsibility-rule p")];
  const coreLayer = chapter.querySelector(".stack-layer--core");

  gsap.set(agentLayer, { opacity: 0.12, clipPath: "inset(100% 0 0 0)", y: 18 });
  gsap.set(connectorLines, { scaleX: 0, transformOrigin: "center" });
  gsap.set(agentCapabilities, { opacity: 0.2 });
  gsap.set(rules, { opacity: 0.24 });

  const timeline = gsap.timeline({
    defaults: { duration: 0.7, ease: "expo.out" },
    scrollTrigger: {
      trigger: chapter,
      start: "top top",
      end: "+=1700",
      pin: scene,
      scrub: 0.65,
      anticipatePin: 1,
      id: "agent-layer",
    },
  });

  timeline
    .to(coreLayer, { y: 10, opacity: 0.82, duration: 0.8 }, 0)
    .to(agentLayer, { opacity: 1, clipPath: "inset(0% 0 0 0)", y: 0 }, 0.2)
    .to(connectorLines, { scaleX: 1, duration: 0.8 }, 0.65)
    .to(agentCapabilities, { opacity: 1, stagger: 0.12, duration: 0.45 }, 1.05)
    .to(rules, { opacity: 1, stagger: 0.3 }, 2.0);
};

const buildPsdAgentScene = () => {
  const chapter = document.querySelector("#psd-agent");
  const scene = chapter.querySelector(".scene");
  const path = chapter.querySelector("#psdFlowPath");
  const nodes = [...chapter.querySelectorAll("[data-flow-step]")];
  const panels = [...chapter.querySelectorAll("[data-evidence-step]")];
  const checkpoint = chapter.querySelector(".checkpoint-mark");
  const retryLoop = chapter.querySelector(".retry-loop");
  const counter = chapter.querySelector("#evidenceCounter");
  const board = chapter.querySelector(".psd-flow-board");
  const consolePanel = chapter.querySelector(".evidence-console");
  setPathReady(path);

  const nodeColors = nodes.map((node) =>
    node.classList.contains("flow-node--gate") ? "#e8c66a" : "#2ee6b2"
  );

  // Each node lights as the drawn line reaches it, so the trace and the evidence panel
  // it belongs to stay together.
  const traceDuration = 9;
  const nodeTimes = nodes.map((node) => fractionAlongPath(path, node) * traceDuration);

  gsap.set([board, consolePanel], { opacity: 0.55, y: 10 });
  gsap.set(nodes, { opacity: 0.3, scale: 0.96, transformOrigin: "center" });
  gsap.set(nodes[0], { opacity: 1 });
  panels.forEach((panel, index) => {
    gsap.set(panel, {
      autoAlpha: index === 0 ? 1 : 0,
      y: index === 0 ? 0 : 16,
      clipPath: index === 0 ? "inset(0% 0 0 0)" : "inset(0 0 14% 0)",
    });
    panel.classList.toggle("is-visible", index === 0);
  });
  gsap.set([checkpoint, retryLoop], { opacity: 0 });

  const timeline = gsap.timeline({
    defaults: { duration: 0.34, ease: "expo.out" },
    onUpdate() {
      // Count the nodes the trace has reached. Panel 0 is on screen at rest, so the
      // readout is 1-based, and it steps with the panel crossfade at nodeTime + 0.04.
      const time = timeline.progress() * timeline.duration();
      let step = 1;
      nodeTimes.forEach((nodeTime, index) => {
        if (time >= nodeTime + 0.04) step = index + 1;
      });
      const total = String(nodeTimes.length).padStart(2, "0");
      counter.textContent = `${String(step).padStart(2, "0")} / ${total}`;
    },
    scrollTrigger: {
      trigger: chapter,
      start: "top top",
      end: "+=6200",
      pin: scene,
      scrub: 0.7,
      anticipatePin: 1,
      invalidateOnRefresh: true,
      id: "psd-agent-flow",
    },
  });

  timeline
    .to([board, consolePanel], { opacity: 1, y: 0, duration: 0.55, stagger: 0.08 }, 0)
    .fromTo(
      path,
      { strokeDashoffset: () => setPathReady(path) },
      { strokeDashoffset: 0, duration: traceDuration, ease: "none" },
      0
    );

  nodes.forEach((node, index) => {
    const position = nodeTimes[index];
    const rect = node.querySelector("rect");
    timeline
      .to(node, { opacity: 1, scale: 1, duration: 0.3 }, position)
      .to(rect, {
        attr: { stroke: nodeColors[index] },
        fill: index === 5 ? "rgba(232,198,106,0.10)" : "rgba(46,230,178,0.09)",
        duration: 0.36,
      }, position);

    if (index > 0) {
      timeline
        .to(panels[index - 1], { autoAlpha: 0, y: -10, duration: 0.2 }, position - 0.06)
        .to(panels[index], {
          autoAlpha: 1,
          y: 0,
          clipPath: "inset(0% 0 0 0)",
          duration: 0.3,
        }, position + 0.04);
    }
  });

  timeline
    .to(checkpoint, { opacity: 1, duration: 0.35 }, 6.05)
    .to(retryLoop, { opacity: 1, duration: 0.5 }, 7.15)
    .to(
      path,
      {
        strokeDasharray: () => {
          const length = getRenderedLength(path);
          return `${length} ${length * 2}`;
        },
        duration: 0.01,
      },
      8.9
    );
};

const buildRealCaseScene = () => {
  const chapter = document.querySelector("#real-case");
  const scene = chapter.querySelector(".scene");
  const columns = [...chapter.querySelectorAll(".case-column")];
  const links = [...chapter.querySelectorAll(".case-link")];

  gsap.set(columns, { opacity: 0.25, clipPath: "inset(0 0 100% 0)" });
  gsap.set(links, { opacity: 0.15 });

  const timeline = gsap.timeline({
    defaults: { duration: 0.65, ease: "expo.out" },
    scrollTrigger: {
      trigger: chapter,
      start: "top top",
      end: "+=1500",
      pin: scene,
      scrub: 0.65,
      anticipatePin: 1,
      id: "real-case",
    },
  });

  timeline
    .to(columns[0], { opacity: 1, clipPath: "inset(0 0 0% 0)" }, 0)
    .to(links[0], { opacity: 1 }, 0.65)
    .to(columns[1], { opacity: 1, clipPath: "inset(0 0 0% 0)" }, 1.05)
    .to(links[1], { opacity: 1 }, 1.75)
    .to(columns[2], { opacity: 0.78, clipPath: "inset(0 0 0% 0)" }, 2.15);
};

const buildProgressScene = () => {
  const chapter = document.querySelector("#progress");
  const scene = chapter.querySelector(".scene");
  const spine = chapter.querySelector("#roadmapProgress");
  const items = [...chapter.querySelectorAll(".roadmap-item")];
  const verification = chapter.querySelector(".verification-strip");

  gsap.set(items, { opacity: 0.22, x: 14 });
  gsap.set(verification, { opacity: 0.22 });

  const timeline = gsap.timeline({
    defaults: { duration: 0.48, ease: "expo.out" },
    scrollTrigger: {
      trigger: chapter,
      start: "top top",
      end: "+=2200",
      pin: scene,
      scrub: 0.65,
      anticipatePin: 1,
      id: "roadmap",
    },
  });

  // The spine marks work that is done, so it has to reach item 05 exactly as item 05
  // settles. Running it over the whole timeline left it trailing the revealed items by
  // more than half the list for most of the scroll.
  const completedItems = 5;
  timeline.to(
    spine,
    { scaleY: 0.55, duration: completedItems * 0.48, ease: "none" },
    0
  );
  items.forEach((item, index) => {
    timeline.to(item, { opacity: index < 5 ? 1 : 0.48, x: 0 }, index * 0.48);
  });
  timeline.to(verification, { opacity: 1, duration: 0.75 }, 4.65);
};

const buildDirectionScene = () => {
  const chapter = document.querySelector("#direction");
  const scene = chapter.querySelector(".scene");
  const path = chapter.querySelector("#multiAgentPath");
  const director = chapter.querySelector(".graph-node--director");
  const agents = [
    chapter.querySelector(".graph-node--psd"),
    chapter.querySelector(".graph-node--unity"),
    chapter.querySelector(".graph-node--validator"),
  ];
  const cores = [
    chapter.querySelector(".graph-node--ps-core"),
    chapter.querySelector(".graph-node--unity-core"),
    chapter.querySelector(".graph-node--evidence"),
  ];
  const rules = chapter.querySelector(".three-rules");
  const quote = chapter.querySelector(".closing-quote");
  const future = chapter.querySelector(".future-note");
  const graph = chapter.querySelector(".multi-agent-graph");

  setPathReady(path);
  gsap.set([director, ...agents, ...cores], { opacity: 0.18, scale: 0.98, transformOrigin: "center" });
  gsap.set([rules, quote, future], { opacity: 0.16 });
  gsap.set(graph, { scale: 1.04, transformOrigin: "center top" });

  const timeline = gsap.timeline({
    defaults: { duration: 0.72, ease: "expo.out" },
    scrollTrigger: {
      trigger: chapter,
      start: "top top",
      end: "+=2400",
      pin: scene,
      scrub: 0.7,
      anticipatePin: 1,
      invalidateOnRefresh: true,
      id: "multi-agent-direction",
    },
  });

  timeline
    .to(graph, { scale: 1, duration: 4.2, ease: "none" }, 0)
    .to(director, { opacity: 1, scale: 1 }, 0)
    .fromTo(
      path,
      { strokeDashoffset: () => setPathReady(path) },
      { strokeDashoffset: 0, duration: 4.2, ease: "none" },
      0.2
    )
    .to(agents, { opacity: 1, scale: 1, stagger: 0.28 }, 0.75)
    .to(cores, { opacity: 1, scale: 1, stagger: 0.24 }, 1.8)
    .to(rules, { opacity: 1 }, 2.7)
    .to(quote, { opacity: 1 }, 3.25)
    .to(future, { opacity: 1 }, 3.75);
};

const setupChapterTracking = () => {
  chapters.forEach((chapter) => {
    ScrollTrigger.create({
      trigger: chapter,
      start: () => `top top+=${parseInt(getComputedStyle(document.documentElement).getPropertyValue("--header-h"), 10) + 8}`,
      end: () => `bottom top+=${parseInt(getComputedStyle(document.documentElement).getPropertyValue("--header-h"), 10) + 8}`,
      onEnter: () => activateChapter(chapter),
      onEnterBack: () => activateChapter(chapter),
      id: `chapter-${chapter.dataset.index}`,
    });
  });
};

const setupGlobalProgress = () => {
  const setProgress = gsap.quickSetter(progressBar, "scaleX");
  ScrollTrigger.create({
    start: 0,
    end: "max",
    onUpdate: (self) => setProgress(self.progress),
    id: "global-progress",
  });
};

const setupNavigation = () => {
  const reduced = () => useReducedMotion();

  document.querySelectorAll('a[href^="#"]').forEach((link) => {
    link.addEventListener("click", (event) => {
      const target = document.querySelector(link.getAttribute("href"));
      if (!target) return;
      event.preventDefault();
      target.scrollIntoView({ behavior: reduced() ? "auto" : "smooth", block: "start" });
    });
  });

  window.addEventListener("keydown", (event) => {
    if (event.defaultPrevented || event.metaKey || event.ctrlKey || event.altKey) return;
    if (event.target instanceof HTMLElement && event.target.closest("a, button, input, textarea, select")) return;

    const key = event.key;
    let destination = null;
    const currentScroll = window.scrollY;
    const shortStep = Math.max(420, window.innerHeight * 0.78);
    const pageStep = Math.max(520, window.innerHeight * 0.94);

    if (key === "ArrowDown") destination = currentScroll + shortStep;
    if (key === "ArrowUp") destination = currentScroll - shortStep;
    if (key === "PageDown" || key === " ") destination = currentScroll + pageStep;
    if (key === "PageUp") destination = currentScroll - pageStep;
    if (key === "Home") destination = 0;
    if (key === "End") destination = document.documentElement.scrollHeight;

    if (destination === null) return;
    event.preventDefault();
    window.scrollTo({ top: destination, behavior: reduced() ? "auto" : "smooth" });
  });
};

const setupResponsiveMotion = () => {
  const media = gsap.matchMedia();

  media.add("(min-width: 901px)", () => {
    if (useReducedMotion()) {
      document.body.classList.add("reduced-motion");
      activateChapter(chapters[0]);
      setupChapterTracking();
      setupGlobalProgress();
      updateMotionToggle();

      return () => document.body.classList.remove("reduced-motion");
    }

    document.body.classList.add("motion-ready");
    buildPreviousScene();
    buildLimitationScene();
    buildAgentLayerScene();
    buildPsdAgentScene();
    buildRealCaseScene();
    buildProgressScene();
    buildDirectionScene();
    setupChapterTracking();
    setupGlobalProgress();
    updateMotionToggle();

    return () => {
      document.body.classList.remove("motion-ready");
    };
  });

  media.add("(max-width: 900px)", () => {
    document.body.classList.add("reduced-motion");
    activateChapter(chapters[0]);
    setupChapterTracking();
    setupGlobalProgress();
    updateMotionToggle();

    return () => {
      document.body.classList.remove("reduced-motion");
    };
  });
};

setupNavigation();
setupResponsiveMotion();
activateChapter(chapters[0]);
updateMotionToggle();

prefersReducedMotion.addEventListener?.("change", () => {
  if (motionPreference === "system") window.location.reload();
});

let resizeTimer;
window.addEventListener("resize", () => {
  window.clearTimeout(resizeTimer);
  resizeTimer = window.setTimeout(() => ScrollTrigger.refresh(), 180);
});

if (document.fonts?.ready) {
  document.fonts.ready.then(() => ScrollTrigger.refresh());
}
