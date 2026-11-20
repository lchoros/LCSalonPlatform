// Forms that only choose what to show (ADR 0087): with this script a change submits them at once and their fallback
// button steps aside; without it the button does the same job. Nothing here decides anything — it presses submit.
for (const form of document.querySelectorAll('form[data-auto-submit]')) {
    for (const fallback of form.querySelectorAll('[data-auto-submit-fallback]')) {
        fallback.hidden = true;
    }

    form.addEventListener('change', () => form.requestSubmit());
}