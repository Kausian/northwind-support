import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { emptyFilters } from '../hooks/useTicketList';
import { TicketFilters } from './TicketFilters';

function renderFilters(filters = emptyFilters) {
  const onChange = vi.fn();
  render(
    <TicketFilters filters={filters} agents={[]} categories={[]} customers={[]} onChange={onChange} onReset={vi.fn()} />,
  );
  return onChange;
}

describe('TicketFilters SLA filter', () => {
  it('offers every SLA state the API knows, plus "all"', () => {
    renderFilters();

    const options = Array.from(screen.getByLabelText('SLA').querySelectorAll('option')).map((o) => [o.value, o.textContent]);

    expect(options).toEqual([
      ['', 'All SLA states'],
      ['WithinSla', 'Within SLA'],
      ['AtRisk', 'At risk'],
      ['Breached', 'Breached'],
      ['Met', 'Met'],
      ['NotApplicable', 'No SLA'],
    ]);
  });

  it('reports the exact API value when a state is chosen', async () => {
    const onChange = renderFilters();

    await userEvent.selectOptions(screen.getByLabelText('SLA'), 'At risk');

    expect(onChange).toHaveBeenCalledWith({ slaStatus: 'AtRisk' });
  });

  it('clears the filter when "All SLA states" is chosen', async () => {
    const onChange = renderFilters({ ...emptyFilters, slaStatus: 'Breached' });

    expect(screen.getByLabelText('SLA')).toHaveValue('Breached');

    await userEvent.selectOptions(screen.getByLabelText('SLA'), 'All SLA states');

    expect(onChange).toHaveBeenCalledWith({ slaStatus: undefined });
  });
});
