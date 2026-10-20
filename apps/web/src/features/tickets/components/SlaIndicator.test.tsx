import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { spokenText } from '../../../test/a11y';
import type { SlaStatus } from '../../../types/api';
import { SlaIndicator } from './SlaIndicator';

const due = '2026-09-24T16:12:00Z';

describe('SlaIndicator', () => {
  it.each<[SlaStatus, string, string]>([
    ['WithinSla', 'Within SLA', '●'],
    ['AtRisk', 'At risk', '▲'],
    ['Breached', 'Breached', '✕'],
    ['Met', 'Met', '✓'],
    ['NotApplicable', 'No SLA', '–'],
  ])('shows %s as "%s" with its own symbol', (status, label, symbol) => {
    const { container } = render(<SlaIndicator slaStatus={status} dueAtUtc={due} />);

    const badge = container.querySelector('.badge');

    // What a screen reader announces: the decorative symbol is hidden, the words are not.
    expect(spokenText(badge)).toBe(`SLA: ${label}`);
    // The badge also carries a symbol per state, so colour is never the only cue.
    expect(badge?.textContent).toBe(`${symbol} SLA: ${label}`);
  });

  it('gives every state a different label, symbol and tone', () => {
    const rendered = (['WithinSla', 'AtRisk', 'Breached', 'Met', 'NotApplicable'] as SlaStatus[]).map((status) => {
      const { container, unmount } = render(<SlaIndicator slaStatus={status} dueAtUtc={due} />);
      const badge = container.querySelector('.badge');
      const result = { text: badge?.textContent, tone: badge?.className };
      unmount();
      return result;
    });

    expect(new Set(rendered.map((r) => r.text)).size).toBe(5);
    expect(new Set(rendered.map((r) => r.tone)).size).toBe(5);
  });

  it('shows the due date when there is one', () => {
    render(<SlaIndicator slaStatus="AtRisk" dueAtUtc={due} />);

    expect(screen.getByText(/^Due /)).toBeInTheDocument();
  });

  it('says there is no due date rather than showing a blank', () => {
    render(<SlaIndicator slaStatus="NotApplicable" dueAtUtc={null} />);

    expect(screen.getByText('No due date')).toBeInTheDocument();
  });
});
