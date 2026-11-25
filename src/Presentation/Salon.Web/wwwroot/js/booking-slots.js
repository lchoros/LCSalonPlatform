// On a phone the days and the times of one part of the day each run in a single line (ADR 0094, stage 5). A tap on a
// day or a time is a new page, and a line starts at its beginning again: this brings what is chosen back into sight.
// Nothing here decides anything; without it the chosen day or time may simply need a slide of the finger to be seen.
for (const chosen of document.querySelectorAll('.day-strip [aria-current="date"], .slot-row .is-chosen')) {
    const row = chosen.closest('.day-strip, .slot-row');
    if (!row || row.scrollWidth <= row.clientWidth) {
        continue;
    }

    const box = row.getBoundingClientRect();
    const item = chosen.getBoundingClientRect();
    row.scrollLeft += item.left - box.left - (box.width - item.width) / 2;
}
