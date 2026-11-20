// Dragging a visit on the admin calendar (ADR 0086). This script only PROPOSES a move: it names a column (person and
// day) and a row (minute of the salon's clock) and posts them with the anti-forgery token; the server re-checks the
// permission, the tenant and the availability engine, and either confirms (204) or explains (409). No time-zone
// arithmetic happens here, and without this file the appointment card's move form does the same job.

const grid = document.querySelector('[data-testid="calendar-grid"]');
if (grid) {
    start(grid);
}

function start(grid) {
    const MOVABLE = new Set(['Booked', 'Confirmed']);
    const MOUSE_SLOP = 5; // px before a press becomes a drag, so a click still opens the card
    const TOUCH_HOLD = 350; // ms a finger rests before a drag starts, so a swipe still scrolls
    const token = document.querySelector('#calendar-drop-token input[name="__RequestVerificationToken"]')?.value;
    const dropPath = grid.dataset.dropPath;
    if (!token || !dropPath) {
        return;
    }

    for (const block of grid.querySelectorAll('.cal-block')) {
        if (MOVABLE.has(block.dataset.status)) {
            block.classList.add('is-draggable');
            block.setAttribute('draggable', 'false'); // the browser's own link-drag would fight ours
        }
    }

    let drag = null;

    grid.addEventListener('pointerdown', (event) => {
        const block = event.target.closest('.cal-block.is-draggable');
        if (!block || drag || (event.pointerType === 'mouse' && event.button !== 0)) {
            return;
        }

        const rows = block.parentElement;
        const firstRow = rows.querySelector('[data-row]');
        if (!firstRow) {
            return;
        }

        drag = {
            block,
            pointerId: event.pointerId,
            startX: event.clientX,
            startY: event.clientY,
            active: false,
            rowHeight: firstRow.getBoundingClientRect().height,
            originRows: rows,
            originRow: parseInt(block.style.gridRowStart, 10),
            span: parseInt((block.style.gridRowEnd || 'span 1').replace('span', ''), 10) || 1,
            blockTop: block.getBoundingClientRect().top,
            hint: null,
            target: null,
            holdTimer: null,
        };

        if (event.pointerType === 'touch') {
            drag.holdTimer = setTimeout(() => activate(event), TOUCH_HOLD);
        }
    });

    grid.addEventListener('pointermove', (event) => {
        if (!drag || event.pointerId !== drag.pointerId) {
            return;
        }

        const dx = event.clientX - drag.startX;
        const dy = event.clientY - drag.startY;
        if (!drag.active) {
            if (event.pointerType === 'touch') {
                if (Math.hypot(dx, dy) > MOUSE_SLOP * 2) {
                    cancel(); // the finger moved before the hold: this is a scroll, not a drag
                }

                return;
            }

            if (Math.hypot(dx, dy) < MOUSE_SLOP) {
                return;
            }

            activate(event);
        }

        drag.block.style.transform = `translate(${dx}px, ${dy}px)`;
        aim(event.clientX, dy);
    });

    grid.addEventListener('pointerup', (event) => {
        if (!drag || event.pointerId !== drag.pointerId) {
            return;
        }

        if (!drag.active) {
            cancel();
            return;
        }

        drop();
    });

    grid.addEventListener('pointercancel', () => (drag?.active ? settleBack() : cancel()));
    document.addEventListener('keydown', (event) => {
        if (event.key === 'Escape' && drag?.active) {
            settleBack();
        }
    });

    // While a drag is live a finger must move the block, not the page; passive listeners cannot say so.
    document.addEventListener('touchmove', (event) => {
        if (drag?.active) {
            event.preventDefault();
        }
    }, { passive: false });

    // The press that ended a drag must not also follow the block's link to its card.
    grid.addEventListener('click', (event) => {
        if (grid.dataset.justDragged === 'true') {
            event.preventDefault();
            event.stopPropagation();
            delete grid.dataset.justDragged;
        }
    }, true);

    function activate(event) {
        if (!drag || drag.active) {
            return;
        }

        drag.active = true;
        drag.block.classList.add('is-dragging');
        try {
            drag.block.setPointerCapture(drag.pointerId);
        } catch {
            // capture is a nicety: without it the grid still receives the moves
        }

        drag.hint = document.createElement('div');
        drag.hint.className = 'cal-drop-hint';
        drag.hint.setAttribute('aria-hidden', 'true');
        aim(event.clientX, event.clientY - drag.startY);
    }

    function aim(clientX, dy) {
        const columns = [...grid.querySelectorAll('[data-testid="calendar-column"]')];
        const column = columns.find((c) => {
            const rect = c.getBoundingClientRect();
            return clientX >= rect.left && clientX < rect.right;
        }) ?? drag.block.closest('[data-testid="calendar-column"]');
        const rows = column.querySelector('[data-row]').parentElement;
        const rowCount = rows.querySelectorAll('[data-row]').length;
        const moved = Math.round(dy / drag.rowHeight);
        const row = Math.min(Math.max(drag.originRow + moved, 1), rowCount - drag.span + 1);
        const cell = rows.querySelector(`[data-row="${row}"]`);

        drag.target = { column, rows, row, minute: cell?.dataset.minute };
        drag.hint.style.gridRow = `${row} / span ${drag.span}`;
        drag.hint.style.gridColumn = '1';
        drag.hint.classList.toggle('is-refused', cell?.dataset.working === 'false');
        if (drag.hint.parentElement !== rows) {
            rows.appendChild(drag.hint);
        }
    }

    async function drop() {
        const { block, target, originRows, originRow } = drag;
        grid.dataset.justDragged = 'true';
        setTimeout(() => delete grid.dataset.justDragged, 80);
        if (!target || target.minute === undefined || (target.rows === originRows && target.row === originRow)) {
            settleBack();
            return;
        }

        // Let the block land where the outline is while the server makes up its mind.
        const landing = drag;
        drag = null;
        landing.hint?.remove();
        block.classList.remove('is-dragging');
        block.style.transform = '';
        block.style.gridRow = `${target.row} / span ${landing.span}`;
        target.rows.appendChild(block);

        let response = null;
        try {
            response = await fetch(`${dropPath}/${block.dataset.appointmentId}/drop`, {
                method: 'POST',
                credentials: 'same-origin',
                headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
                body: new URLSearchParams({
                    staffMemberId: target.column.dataset.staffId,
                    date: target.column.dataset.date,
                    minute: target.minute,
                    __RequestVerificationToken: token,
                }),
            });
        } catch {
            response = null;
        }

        if (response?.status === 204) {
            window.location.reload(); // the server queued the confirmation; the reload shows it and the true grid
            return;
        }

        // Refused: put the block back where it truly is and say why in the server's own words.
        block.style.gridRow = `${originRow} / span ${landing.span}`;
        originRows.appendChild(block);
        say(response && response.status === 409 ? await response.text() : grid.dataset.refused);
    }

    function settleBack() {
        if (!drag) {
            return;
        }

        const { block, hint } = drag;
        drag = null;
        hint?.remove();
        block.classList.remove('is-dragging'); // the transition comes back, so the block glides home
        block.style.transform = '';
        grid.dataset.justDragged = 'true';
        setTimeout(() => delete grid.dataset.justDragged, 80);
    }

    function cancel() {
        if (drag?.holdTimer) {
            clearTimeout(drag.holdTimer);
        }

        drag = null;
    }

    function say(message) {
        document.querySelector('[data-testid="drag-message"]')?.remove();
        const note = document.createElement('p');
        note.className = 'notice notice-error';
        note.setAttribute('role', 'alert');
        note.dataset.testid = 'drag-message';
        note.textContent = message;
        grid.before(note);
        setTimeout(() => note.remove(), 8000);
    }
}