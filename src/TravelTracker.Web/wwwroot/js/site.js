// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// Copy-to-clipboard for read-only reference text (e.g. the trip code on Trip
// Details). Any button with data-copy-target="#selector" copies that element's
// text. The text stays selectable (user-select-all) if the Clipboard API is
// unavailable (non-secure context), so manual copy still works.
document.addEventListener("click", async (e) => {
    const btn = e.target.closest("[data-copy-target]");
    if (!btn) return;
    const el = document.querySelector(btn.dataset.copyTarget);
    if (!el || !navigator.clipboard) return;
    try {
        await navigator.clipboard.writeText(el.textContent.trim());
        const label = btn.textContent;
        btn.textContent = "Copied";
        setTimeout(() => { btn.textContent = label; }, 1500);
    } catch {
        // Clipboard denied: leave the text selectable for a manual copy.
    }
});
