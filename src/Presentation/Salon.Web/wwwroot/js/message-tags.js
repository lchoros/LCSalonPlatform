// The edit page of a message (ADR 0098). Without this script the tags are a list to read, the preview is the one the
// page came with and the Preview button asks the server for another. With it, a tap on a tag puts the tag where the
// cursor was in the box last used, and the preview and the count of texts follow every key. Nothing here decides what
// may be saved: the server checks the same placeholders again when the form is sent.
const form = document.querySelector('[data-message-form]');

// The basic phone alphabet and its two-place signs, as in SmsLengthRules (Salon.Core/Messaging/SmsLength.cs).
const GSM_BASIC = '@£$¥èéùìòÇ\nØø\rÅåΔ_ΦΓΛΩΠΨΣΘΞÆæßÉ !"#¤%&\'()*+,-./0123456789:;<=>?¡ABCDEFGHIJKLMNOPQRSTUVWXYZÄÖÑÜ§¿abcdefghijklmnopqrstuvwxyzäöñüà';
const GSM_EXTENDED = '^{}\\[~]|€\f';

// The line breaks string.ReplaceLineEndings knows: CR LF, CR, LF, form feed, NEL, and the line and paragraph separators.
const LINE_BREAKS = new RegExp('\r\n|[\r\n\f' + String.fromCharCode(0x85, 0x2028, 0x2029) + ']', 'g');

if (form) {
    start(form);
}

function start(form) {
    // The server's own pattern, so the browser finds exactly the placeholders the server does.
    const pattern = new RegExp(form.dataset.pattern, 'g');
    const unknownLabel = form.dataset.unknownLabel ?? '';
    const chips = [...form.querySelectorAll('[data-tag]')];
    const samples = new Map(chips.map((chip) => [chip.dataset.tag.toLowerCase(), chip.dataset.sample ?? '']));
    const boxes = [...form.querySelectorAll('[data-tag-target]')];
    const used = new Set();
    let last = form.querySelector('#body') ?? boxes[0];

    for (const element of form.querySelectorAll('[data-when-script]')) {
        element.hidden = false;
    }

    for (const element of form.querySelectorAll('[data-when-noscript]')) {
        element.hidden = true;
    }

    form.querySelector('[data-tag-chips]')?.setAttribute('data-ready', '');

    for (const box of boxes) {
        box.addEventListener('focus', () => {
            last = box;
            used.add(box);
        });
        box.addEventListener('input', () => show(box));
    }

    for (const chip of chips) {
        chip.disabled = false;
        // A tap must not take the focus from the box: on a phone the keyboard stays up and the cursor stays put.
        chip.addEventListener('mousedown', (event) => event.preventDefault());
        chip.addEventListener('click', () => {
            if (last) {
                insert(last, '{{' + chip.dataset.tag + '}}', used.has(last));
            }
        });
    }

    function show(box) {
        const preview = form.querySelector(`[data-preview-of="${box.id}"]`);
        if (!preview) {
            return;
        }

        const parts = split(box.value);
        preview.replaceChildren(...parts.map(node));
        const count = preview.parentElement.querySelector('[data-sms-count]');
        if (count) {
            const sent = parts.filter((part) => part.kind !== 'unknown').map((part) => part.text).join('');
            const length = measure(sent);
            count.dataset.segments = String(length.segments);
            count.textContent = count.dataset.format.replace('{0}', String(length.segments)).replace('{1}', String(length.characters));
            const unicode = preview.parentElement.querySelector('[data-sms-unicode]');
            if (unicode) {
                unicode.hidden = !length.unicode;
            }
        }
    }

    // The pieces MessagePreviewRenderer makes: words, a value the message offers, or a placeholder it does not.
    function split(template) {
        const parts = [];
        let at = 0;
        const text = (value) => {
            if (value) {
                parts.push({ kind: 'text', text: value });
            }
        };

        for (const match of template.matchAll(pattern)) {
            text(template.slice(at, match.index));
            at = match.index + match[0].length;
            if (match.groups.escape) {
                text(match[0].slice(1));
                continue;
            }

            const sample = samples.get(match.groups.name.toLowerCase());
            parts.push(sample === undefined ? { kind: 'unknown', text: match[0] } : { kind: 'value', text: sample });
        }

        text(template.slice(at));
        return parts;
    }

    function node(part) {
        if (part.kind === 'text') {
            return document.createTextNode(part.text);
        }

        const mark = document.createElement('mark');
        if (part.kind === 'value') {
            mark.className = 'tag-value';
            mark.textContent = part.text;
            return mark;
        }

        mark.className = 'tag-unknown';
        mark.append(part.text + ' ');
        const note = document.createElement('span');
        note.className = 'tag-unknown-note';
        note.textContent = unknownLabel;
        mark.append(note);
        return mark;
    }

    function insert(box, tag, hasCursor) {
        // A box never used has no cursor of its own yet: the tag goes at the end.
        const start = hasCursor ? box.selectionStart : box.value.length;
        const end = hasCursor ? box.selectionEnd : box.value.length;
        const before = box.value.slice(0, start);
        const after = box.value.slice(end);
        // A space between the tag and a word it would otherwise touch; none before punctuation or a line break.
        const lead = before === '' || /[\s([]$/.test(before) ? '' : ' ';
        const trail = after === '' || /^[\s.,!?:;)\]]/.test(after) ? '' : ' ';
        box.focus();
        box.setRangeText(lead + tag + trail, start, end, 'end');
        box.dispatchEvent(new Event('input', { bubbles: true }));
    }
}

// SmsLengthRules.Measure: 160 characters in one text or 153 each when split; one character outside the basic alphabet
// (a Polish letter, an emoji, a curly quote) makes it 70, or 67 each. Counted in UTF-16 units, as the network does.
function measure(text) {
    if (!text) {
        return { characters: 0, segments: 0, unicode: false };
    }

    const message = text.replace(LINE_BREAKS, '\n');
    let septets = 0;
    for (let i = 0; i < message.length; i++) {
        const c = message.charAt(i);
        if (GSM_BASIC.includes(c)) {
            septets++;
        } else if (GSM_EXTENDED.includes(c)) {
            septets += 2;
        } else {
            return { characters: message.length, segments: parts(message.length, 70, 67), unicode: true };
        }
    }

    return { characters: message.length, segments: parts(septets, 160, 153), unicode: false };
}

function parts(units, single, multi) {
    return units <= single ? 1 : Math.ceil(units / multi);
}