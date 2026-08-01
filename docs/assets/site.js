const chips = document.querySelectorAll("[data-filter]");
const cards = document.querySelectorAll("[data-category]");

for (const chip of chips) {
  chip.addEventListener("click", () => {
    const filter = chip.dataset.filter;
    for (const candidate of chips) {
      const selected = candidate === chip;
      candidate.classList.toggle("selected", selected);
      candidate.setAttribute("aria-pressed", String(selected));
    }
    for (const card of cards) {
      card.classList.toggle(
        "hidden",
        filter !== "all" && card.dataset.category !== filter,
      );
    }
  });
}

const copyButton = document.querySelector("[data-copy]");
copyButton?.addEventListener("click", async () => {
  try {
    await navigator.clipboard.writeText(copyButton.dataset.copy ?? "");
    copyButton.textContent = "Copied";
    window.setTimeout(() => { copyButton.textContent = "Copy"; }, 1600);
  } catch {
    copyButton.textContent = "Select command";
  }
});
