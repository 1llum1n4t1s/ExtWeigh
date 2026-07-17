document.documentElement.classList.add("js");

const instrument = document.querySelector(".instrument");
const modeButtons = [...document.querySelectorAll(".mode-button")];
const cpuValue = document.querySelector("#cpu-value");
const tasksValue = document.querySelector("#tasks-value");
const reducedMotion = window.matchMedia("(prefers-reduced-motion: reduce)").matches;

const readings = {
  all: { cpu: "1,842", tasks: "14" },
  "without-b": { cpu: "1,092", tasks: "6" },
};

function setReading(mode) {
  if (!instrument || !readings[mode]) return;
  instrument.dataset.mode = mode;
  modeButtons.forEach((button) => {
    const active = button.dataset.mode === mode;
    button.classList.toggle("is-active", active);
    button.setAttribute("aria-pressed", String(active));
  });
  if (cpuValue?.firstChild) cpuValue.firstChild.textContent = readings[mode].cpu;
  if (tasksValue?.firstChild) tasksValue.firstChild.textContent = readings[mode].tasks;
}

let demoTimer;
function startDemo() {
  if (reducedMotion || !instrument) return;
  window.clearInterval(demoTimer);
  demoTimer = window.setInterval(() => {
    setReading(instrument.dataset.mode === "without-b" ? "all" : "without-b");
  }, 3800);
}

modeButtons.forEach((button) => {
  button.addEventListener("click", () => {
    setReading(button.dataset.mode);
    startDemo();
  });
});

instrument?.addEventListener("mouseenter", () => window.clearInterval(demoTimer));
instrument?.addEventListener("mouseleave", startDemo);
instrument?.addEventListener("focusin", () => window.clearInterval(demoTimer));
instrument?.addEventListener("focusout", startDemo);

setReading("all");
startDemo();

const revealItems = document.querySelectorAll(".reveal");
if (reducedMotion || !("IntersectionObserver" in window)) {
  revealItems.forEach((item) => item.classList.add("is-visible"));
} else {
  const observer = new IntersectionObserver((entries) => {
    entries.forEach((entry) => {
      if (!entry.isIntersecting) return;
      entry.target.classList.add("is-visible");
      observer.unobserve(entry.target);
    });
  }, { threshold: 0.14 });
  revealItems.forEach((item) => observer.observe(item));
}
