// The time page of a booking (ADR 0094, stage 6). Every day, arrow, time and person on it is a plain link to the same
// page with another address, and without this script that is what they stay. With it, a tap on one of them fetches
// that page and puts its content in place: no loading bar across the top of a phone, no screen that jumps to its top
// and back (the owner, 2026-11-25). The server still decides everything; this only moves what it sent into the page.
// Anything that goes wrong — no network, an answer that is not this page — falls back to following the link.
const page = document.querySelector('[data-booking]');

// On a phone the days and the times of one part of the day each run in a single line; bring what is chosen into sight.
function showChosen(root) {
    for (const chosen of root.querySelectorAll('.day-strip [aria-current="date"], .slot-row .is-chosen')) {
        const row = chosen.closest('.day-strip, .slot-row');
        if (!row || row.scrollWidth <= row.clientWidth) {
            continue;
        }

        const box = row.getBoundingClientRect();
        const item = chosen.getBoundingClientRect();
        if (item.left < box.left || item.right > box.right) {
            row.scrollLeft += item.left - box.left - (box.width - item.width) / 2;
        }
    }
}

// The same days as before (a day or a time was tapped, not an arrow): the line of days stays where the finger left it.
function keepDays(before, root) {
    const after = root.querySelector('.day-strip');
    if (before && after && before.first === after.querySelector('a')?.getAttribute('href')?.split('?')[1]?.split('&')[1]) {
        after.scrollLeft = before.left;
    }
}

async function go(link) {
    const url = new URL(link.href);
    const strip = page.querySelector('.day-strip');
    // The second value of a day's address is the first day of the window ("from"): the same one means the same days.
    const before = strip && { first: strip.querySelector('a')?.getAttribute('href')?.split('?')[1]?.split('&')[1], left: strip.scrollLeft };
    page.setAttribute('aria-busy', 'true');
    try {
        const response = await fetch(url, { headers: { Accept: 'text/html' }, credentials: 'same-origin' });
        const next = response.ok && !response.redirected
            ? new DOMParser().parseFromString(await response.text(), 'text/html').querySelector('[data-booking]')
            : null;
        if (!next) {
            location.assign(url);
            return;
        }

        const fromSheet = location.hash === '#who';
        page.replaceChildren(...next.childNodes);
        page.dataset.arrived = 'true';
        history.replaceState(null, '', url.pathname + url.search);
        if (fromSheet) {
            // An answer given in the sheet closes it: the address has to stop pointing at it for the browser to see that.
            location.replace('#days');
        }

        keepDays(before, page);
        showChosen(page);
    } catch {
        location.assign(url);
    } finally {
        page.removeAttribute('aria-busy');
    }
}

if (page) {
    showChosen(page);
    page.addEventListener('click', event => {
        const link = event.target instanceof Element ? event.target.closest('a[href]') : null;
        if (!link || event.defaultPrevented || event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey || link.target) {
            return;
        }

        const url = new URL(link.href);
        // Only this page with another choice. Another page (the details, the service list) and a link that only
        // opens or closes something here (#who, #discard) are the browser's own business.
        if (url.origin !== location.origin || url.pathname !== location.pathname || url.search === location.search) {
            return;
        }

        event.preventDefault();
        go(link);
    });
}
