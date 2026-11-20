# 0089. A module's pages as one row of tabs

Status: Decided — owner may veto
Date: 2026-11-20
Issue: the owner's look at Versum and Booksy — "they got some nice menu … inventory tab on mobile"; the admin has grown pages that only a button somewhere on another page leads to

## Context

The menu names nine areas. Inside them live sixteen pages: the diary has a day list, a calendar, a waiting list and
an import; the till has cash-up, counter sale and vouchers; services have a price list, categories and an import;
and so on. Until now a page beyond the first was reached by a button on its sibling — different in place and wording
on every page, and often absent in the other direction. On a phone, where the menu is behind the hamburger, getting
from the waiting list back to the calendar took three taps.

## Decision

- **One row of underlined tabs at the top of every page of a module** (`_SectionTabs`), in the brand colour, the page
  being shown marked with `aria-current="page"`. The menu still names the module; the tabs name its pages.
- **The rows are declared once, in `AdminSections`** (Web.Framework): key, label key, address and the permissions the
  page's own controller asks for. A view asks for its row by name — `AdminSections.Till("vouchers")` — and holds no
  list of its own.
- **A tab is drawn only for a role that may open its page; a row left with one tab is not drawn.** A receptionist
  never sees a tab that would lead to the refusal page.
- **On a phone the row scrolls sideways inside its own box**, never wrapping into a wall and never widening the page.
- **Additive.** The buttons that linked sibling pages stay where they are for now; the settings pages keep their own
  wrapped tiles (twelve pages are too many for one line).

## Consequences

- Sixteen pages gain a constant way to their siblings; the phone's three taps become one.
- A new page of a module is one line in `AdminSections` and one line in its view. An integration test walks every
  tab as every role and fails if the row and the page's permissions ever disagree — in either direction — and an
  architecture test holds every label to both languages, since keys declared in code are invisible to the scan of
  the views.
- The old sibling buttons are now duplicates; removing them is a follow-up once the owner has lived with the tabs.

## Alternatives considered

- Sub-items in the sidebar (Booksy's tree): doubles the drawer's height on a phone and hides the siblings again
  behind the hamburger.
- A dropdown ("More…") per page: one more tap, and the current place is not visible at a glance.
- Reading the permissions from the controllers by reflection: exact, but the view layer would then depend on
  controller types across slices; the walking test gives the same guarantee without the coupling.
