import { useCallback, useEffect, useMemo, useState } from 'react';
import { ApiError } from '../../../api/client';
import { ticketsApi } from '../../../api/tickets';
import type { PagedResult, TicketFilters, TicketListItem } from '../../../types/api';

const DEBOUNCE_MS = 300;

export const emptyFilters: TicketFilters = {
  search: '',
  status: undefined,
  priority: undefined,
  categoryId: undefined,
  customerId: undefined,
  assignedAgentId: undefined,
  unassignedOnly: false,
  page: 1,
  pageSize: 20,
  sortBy: 'createdAtUtc',
  sortDirection: 'desc',
};

/** One fetch of the list: the filters it was for, and a counter so a retry is a new fetch. */
interface ListRequest {
  query: TicketFilters;
  attempt: number;
}

/** What the last finished fetch produced, and which request it answered. */
interface ListResult {
  request: ListRequest;
  data?: PagedResult<TicketListItem>;
  error?: string;
}

/**
 * Owns the ticket list's filters and the data they produce.
 *
 * `query` is what the API is asked for, and every change to it is fetched straight away. The
 * search box is the one exception: what the user types is held in `searchText` and only copied
 * into `query` once typing has paused, so there is no request per keystroke.
 */
export function useTicketList() {
  const [query, setQuery] = useState<TicketFilters>(emptyFilters);
  const [searchText, setSearchText] = useState(emptyFilters.search);
  const [attempt, setAttempt] = useState(0);
  const [result, setResult] = useState<ListResult>();

  const request = useMemo<ListRequest>(() => ({ query, attempt }), [query, attempt]);

  useEffect(() => {
    // Cleared when a newer request starts, so a slow response cannot overwrite a newer one.
    let isCurrent = true;

    ticketsApi.getTickets(request.query).then(
      (data) => {
        if (isCurrent) {
          setResult({ request, data });
        }
      },
      (error: unknown) => {
        if (isCurrent) {
          const message = error instanceof ApiError ? error.message : 'Unable to load tickets.';
          setResult((previous) => ({ request, data: previous?.data, error: message }));
        }
      },
    );

    return () => {
      isCurrent = false;
    };
  }, [request]);

  useEffect(() => {
    if (searchText === query.search) {
      return undefined;
    }

    const handle = window.setTimeout(
      () => setQuery((current) => ({ ...current, search: searchText, page: 1 })),
      DEBOUNCE_MS,
    );

    return () => window.clearTimeout(handle);
  }, [searchText, query.search]);

  const updateFilters = useCallback((patch: Partial<TicketFilters>) => {
    const { search, ...rest } = patch;

    if (search !== undefined) {
      setSearchText(search);
    }

    if (Object.keys(rest).length > 0) {
      setQuery((current) => ({ ...current, ...rest, page: rest.page ?? 1 }));
    }
  }, []);

  const resetFilters = useCallback(() => {
    setSearchText(emptyFilters.search);
    setQuery(emptyFilters);
  }, []);

  const reload = useCallback(() => setAttempt((current) => current + 1), []);

  // Still loading until the latest request has answered; the previous page stays on screen.
  const isLoading = result?.request !== request;

  return {
    filters: { ...query, search: searchText },
    updateFilters,
    resetFilters,
    reload,
    isLoading,
    data: result?.data,
    error: isLoading ? undefined : result?.error,
  };
}
