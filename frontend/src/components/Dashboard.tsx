import { useEffect, useState, useMemo } from 'react';
import type { TokenCardDto, QuotePayload } from '../types';
import { toTokenCardDto } from '../types';
import connection, { startConnection } from '../lib/signalr';
import TokenCard from './TokenCard';

interface Props {
  onNavigate: (page: string, tokenId?: string) => void;
}

type SortKey = 'spread' | 'symbol' | 'updated';
type StatusFilter = 'all' | 'active' | 'stale' | 'error';

export default function Dashboard({ onNavigate }: Props) {
  const [tokens, setTokens] = useState<TokenCardDto[]>([]);
  const [search, setSearch] = useState('');
  const [sortBy, setSortBy] = useState<SortKey>('spread');
  const [filterStatus, setFilterStatus] = useState<StatusFilter>('all');

  useEffect(() => {
    fetch('/api/v1/tokens')
      .then(res => res.json())
      .then(setTokens)
      .catch(console.error);

    startConnection();

    connection.on('token.quote', (p: QuotePayload | TokenCardDto) => {
      if ('token_id' in p) {
        setTokens(prev => {
          const idx = prev.findIndex(t => t.id === p.token_id);
          const dto = toTokenCardDto(p, idx >= 0 ? prev[idx] : undefined);
          if (idx >= 0) {
            const next = [...prev];
            next[idx] = dto;
            return next;
          }
          return [...prev, dto];
        });
      } else {
        const dto = p as TokenCardDto;
        setTokens(prev => {
          const idx = prev.findIndex(t => t.id === dto.id);
          if (idx >= 0) {
            const next = [...prev];
            next[idx] = dto;
            return next;
          }
          return [...prev, dto];
        });
      }
    });

    return () => { connection.off('token.quote'); };
  }, []);

  const filtered = useMemo(() => {
    let result = tokens.filter(t =>
      t.symbol.toLowerCase().includes(search.toLowerCase()));

    if (filterStatus === 'active') result = result.filter(t => t.status === 'Active');
    else if (filterStatus === 'stale') result = result.filter(t => t.status === 'StaleBingx' || t.status === 'StaleJupiter');
    else if (filterStatus === 'error') result = result.filter(t => t.status === 'ProxyError' || t.status === 'NoQuote' || t.status === 'Disabled');

    if (sortBy === 'spread') result.sort((a, b) => b.spreadPct - a.spreadPct);
    else if (sortBy === 'symbol') result.sort((a, b) => a.symbol.localeCompare(b.symbol));
    else if (sortBy === 'updated') result.sort((a, b) => (b.lastUpdated ?? '').localeCompare(a.lastUpdated ?? ''));

    return result;
  }, [tokens, search, sortBy, filterStatus]);

  return (
    <div className="p-4 max-w-4xl mx-auto">
      <div className="flex items-center justify-between mb-4">
        <h1 className="text-2xl font-bold text-white">Onix Scanner</h1>
        <input
          type="text"
          placeholder="Search token..."
          value={search}
          onChange={e => setSearch(e.target.value)}
          className="px-3 py-1.5 bg-gray-800 border border-gray-700 rounded text-sm w-48"
        />
      </div>
      <div className="flex gap-2 mb-4 flex-wrap">
        <select value={sortBy} onChange={e => setSortBy(e.target.value as SortKey)}
          className="px-3 py-1 bg-gray-800 border border-gray-700 rounded text-sm">
          <option value="spread">Sort by Spread</option>
          <option value="symbol">Sort by Symbol</option>
          <option value="updated">Sort by Updated</option>
        </select>
        <select value={filterStatus} onChange={e => setFilterStatus(e.target.value as StatusFilter)}
          className="px-3 py-1 bg-gray-800 border border-gray-700 rounded text-sm">
          <option value="all">All</option>
          <option value="active">Active</option>
          <option value="stale">Stale</option>
          <option value="error">Error</option>
        </select>
        <button onClick={() => onNavigate('settings')}
          className="px-3 py-1 bg-gray-800 rounded text-sm hover:bg-gray-700">Settings</button>
      </div>
      <div className="flex flex-col gap-3">
        {filtered.map(t => (
          <TokenCard key={t.id} token={t} onClickChart={(id) => onNavigate('chart', id)} />
        ))}
      </div>
    </div>
  );
}
