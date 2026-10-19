import { Link } from 'react-router-dom';
import { EmptyState } from '../../components/EmptyState';
import { ErrorMessage } from '../../components/ErrorMessage';
import { Pagination } from '../../components/Pagination';
import { Spinner } from '../../components/Spinner';
import { useReferenceData } from '../../hooks/useReferenceData';
import { TicketFilters } from './components/TicketFilters';
import { TicketTable } from './components/TicketTable';
import { useTicketList } from './hooks/useTicketList';

export function TicketListPage() {
  const { filters, updateFilters, resetFilters, reload, isLoading, data, error } = useTicketList();
  const reference = useReferenceData();

  return (
    <section>
      <header className="page-header">
        <div>
          <h1>Tickets</h1>
          <p className="page-header__subtitle">
            Everything the team is working on, newest first.
          </p>
        </div>

        <Link className="button button--primary" to="/tickets/new">
          New ticket
        </Link>
      </header>

      <TicketFilters
        filters={filters}
        agents={reference.data?.agents ?? []}
        categories={reference.data?.categories ?? []}
        customers={reference.data?.customers ?? []}
        onChange={updateFilters}
        onReset={resetFilters}
      />

      {error && <ErrorMessage message={error} onRetry={reload} />}

      {isLoading && !data && <Spinner label="Loading tickets" />}

      {data && data.items.length === 0 && (
        <EmptyState
          title="No tickets match these filters"
          description="Try clearing a filter or searching for something else."
        />
      )}

      {data && data.items.length > 0 && (
        <>
          <div className="table-wrapper" aria-busy={isLoading}>
            <TicketTable tickets={data.items} />
          </div>

          <Pagination
            page={data.page}
            pageSize={data.pageSize}
            totalCount={data.totalCount}
            totalPages={data.totalPages}
            onPageChange={(page) => updateFilters({ page })}
          />
        </>
      )}
    </section>
  );
}
