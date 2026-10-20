/** An element's text as a screen reader hears it: without anything marked aria-hidden. */
export function spokenText(element: Element | null): string {
  const clone = element?.cloneNode(true) as Element | undefined;
  clone?.querySelectorAll('[aria-hidden="true"]').forEach((hidden) => hidden.remove());
  return clone?.textContent?.replace(/\s+/g, ' ').trim() ?? '';
}
