// Admin email template editor (Pages/Admin/EmailTemplates/Edit, ADR-0005).
// - Token buttons insert {{Token}} at the cursor of the last-focused field.
// - Live preview: debounced POST of the draft to ?handler=Preview; the server
//   sanitizes + renders with sample data, and the HTML goes into a sandboxed
//   iframe (no scripts, no same-origin), so a broken template can't touch this page.
(() => {
    const form = document.getElementById("template-form");
    if (!form) return;

    const fields = form.querySelectorAll("[data-token-target]");
    let lastField = form.querySelector("textarea[data-token-target]");
    fields.forEach(f => f.addEventListener("focus", () => { lastField = f; }));

    form.addEventListener("click", e => {
        const btn = e.target.closest("[data-insert-token]");
        if (!btn || !lastField) return;
        const token = btn.dataset.insertToken;
        const start = lastField.selectionStart ?? lastField.value.length;
        const end = lastField.selectionEnd ?? start;
        lastField.value = lastField.value.slice(0, start) + token + lastField.value.slice(end);
        lastField.focus();
        lastField.setSelectionRange(start + token.length, start + token.length);
        schedulePreview();
    });

    const frame = document.getElementById("preview-frame");
    const subject = document.getElementById("preview-subject");
    const errors = document.getElementById("preview-errors");
    const url = new URL(form.action, window.location.href);
    url.searchParams.set("handler", "Preview");

    let timer;
    let seq = 0;
    function schedulePreview() {
        clearTimeout(timer);
        timer = setTimeout(refreshPreview, 400);
    }

    async function refreshPreview() {
        const mine = ++seq;
        try {
            const res = await fetch(url, { method: "POST", body: new FormData(form) });
            if (!res.ok || mine !== seq) return;
            const data = await res.json();
            if (mine !== seq) return;
            subject.textContent = data.subject ?? "";
            if (data.html != null) frame.srcdoc = data.html;
            errors.replaceChildren(...(data.errors ?? []).map(msg => {
                const li = document.createElement("li");
                li.textContent = msg;
                return li;
            }));
        } catch {
            // Preview is a convenience; Save still validates server-side.
        }
    }

    fields.forEach(f => f.addEventListener("input", schedulePreview));
})();
