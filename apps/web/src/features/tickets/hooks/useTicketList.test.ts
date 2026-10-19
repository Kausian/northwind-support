import { act, renderHook } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ticketsApi } from '../../../api/tickets';
import { page, ticketListItem } from '../../../test/fixtures';
import type { PagedResult, TicketFilters, TicketListItem } from '../../../types/api';
import { useTicketList } from './useTicketList';

vi.mock('../../../api/tickets', () => ({
  ticketsApi: {
    getTickets: vi.fn(),
  },
}));

const getTickets = vi.mocked(ticketsApi.getTickets);

/** Lets pending timers and promises run, without moving past the search debounce. */
async function flush(ms = 0) {
  await act(async () => {
    await vi.advanceTimersByTimeAsync(ms);
  });
}

/** Renders the hook and waits for its first load, so tests start from a settled list. */
async function renderLoaded() {
  const hook = renderHook(() => useTicketList());
  await flush(300);
  getTickets.mockClear();
  return hook;
}

describe('useTicketList', () => {
  beforeEach(() => {
    vi.useFakeTimers();
    getTickets.mockResolvedValue(page([ticketListItem()]));
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.clearAllMocks();
  });

  it('searches with the text the user typed, once the typing settles', async () => {
    const { result } = renderHook(() => useTicketList());

    await act(async () => {
      await vi.advanceTimersByTimeAsync(300);
    });

    expect(getTickets).toHaveBeenCalledTimes(1);

    act(() => {
      result.current.updateFilters({ search: 'invoice' });
    });

    // Still inside the debounce window: no second request yet.
    expect(getTickets).toHaveBeenCalledTimes(1);

    await act(async () => {
      await vi.advanceTimersByTimeAsync(300);
    });

    expect(getTickets).toHaveBeenCalledTimes(2);
    expect(getTickets).toHaveBeenLastCalledWith(
      expect.objectContaining({ search: 'invoice', page: 1 }),
    );

    expect(result.current.isLoading).toBe(false);
    expect(result.current.data?.items).toHaveLength(1);
  });

  // DEFECT-117: only search and paging used to trigger a request.
  it.each<[string, Partial<TicketFilters>]>([
    ['status', { status: 'InProgress' }],
    ['priority', { priority: 'High' }],
    ['category', { categoryId: 3 }],
    ['customer', { customerId: 2 }],
    ['assigned agent', { assignedAgentId: 4 }],
    ['unassigned only', { unassignedOnly: true }],
    ['sort', { sortBy: 'dueAtUtc', sortDirection: 'asc' }],
  ])('requests immediately when the %s filter changes', async (_, patch) => {
    const { result } = await renderLoaded();

    act(() => {
      result.current.updateFilters(patch);
    });
    await flush();

    expect(getTickets).toHaveBeenCalledTimes(1);
    expect(getTickets).toHaveBeenLastCalledWith(expect.objectContaining({ ...patch, page: 1 }));
  });

  it('sends one search request after typing settles, not one per keystroke', async () => {
    const { result } = await renderLoaded();

    for (const text of ['i', 'in', 'inv', 'invo']) {
      act(() => {
        result.current.updateFilters({ search: text });
      });
      await flush(100);
    }

    expect(getTickets).not.toHaveBeenCalled();
    // The input still shows every keystroke straight away.
    expect(result.current.filters.search).toBe('invo');

    await flush(300);

    expect(getTickets).toHaveBeenCalledTimes(1);
    expect(getTickets).toHaveBeenLastCalledWith(expect.objectContaining({ search: 'invo' }));
  });

  it('keeps the status filter when searching after it was changed', async () => {
    const { result } = await renderLoaded();

    act(() => {
      result.current.updateFilters({ status: 'InProgress' });
    });
    await flush();
    act(() => {
      result.current.updateFilters({ search: 'printer' });
    });
    await flush(300);

    expect(getTickets).toHaveBeenCalledTimes(2);
    expect(getTickets).toHaveBeenLastCalledWith(
      expect.objectContaining({ status: 'InProgress', search: 'printer' }),
    );
  });

  it('still reacts to filter changes after the search is cleared', async () => {
    const { result } = await renderLoaded();

    act(() => {
      result.current.updateFilters({ search: 'printer' });
    });
    await flush(300);
    act(() => {
      result.current.updateFilters({ search: '' });
    });
    await flush(300);

    expect(getTickets).toHaveBeenLastCalledWith(expect.objectContaining({ search: '' }));
    getTickets.mockClear();

    act(() => {
      result.current.updateFilters({ status: 'InProgress' });
    });
    await flush();

    expect(getTickets).toHaveBeenCalledTimes(1);
    expect(getTickets).toHaveBeenLastCalledWith(
      expect.objectContaining({ status: 'InProgress', search: '' }),
    );
  });

  it('requests immediately when the filters are cleared', async () => {
    const { result } = await renderLoaded();

    act(() => {
      result.current.updateFilters({ status: 'Open' });
    });
    await flush();
    getTickets.mockClear();

    act(() => {
      result.current.resetFilters();
    });
    await flush();

    expect(getTickets).toHaveBeenCalledTimes(1);
    expect(getTickets).toHaveBeenLastCalledWith(
      expect.objectContaining({ status: undefined, search: '' }),
    );
  });

  it('shows a failure and fetches the same filters again on retry', async () => {
    getTickets.mockRejectedValueOnce(new Error('offline'));
    const { result } = renderHook(() => useTicketList());
    await flush();

    expect(result.current.error).toBe('Unable to load tickets.');
    expect(result.current.isLoading).toBe(false);

    act(() => {
      result.current.reload();
    });
    await flush();

    expect(getTickets).toHaveBeenCalledTimes(2);
    expect(result.current.error).toBeUndefined();
    expect(result.current.data?.items).toHaveLength(1);
  });

  it('ignores a slow response that a newer request has overtaken', async () => {
    const { result } = await renderLoaded();

    let resolveSlow: (value: PagedResult<TicketListItem>) => void = () => {};
    getTickets.mockImplementationOnce(
      () => new Promise((resolve) => (resolveSlow = resolve)),
    );
    getTickets.mockResolvedValueOnce(page([ticketListItem({ id: 2, reference: 'TCK-0002' })]));

    act(() => {
      result.current.updateFilters({ search: 'first' });
    });
    await flush(300);
    act(() => {
      result.current.updateFilters({ search: 'second' });
    });
    await flush(300);

    expect(result.current.data?.items[0].reference).toBe('TCK-0002');

    // The first request finally answers, after the second one.
    await act(async () => {
      resolveSlow(page([ticketListItem({ id: 1, reference: 'TCK-0001' })]));
    });

    expect(result.current.data?.items[0].reference).toBe('TCK-0002');
    expect(result.current.isLoading).toBe(false);
  });
});
