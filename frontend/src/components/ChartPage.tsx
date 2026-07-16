import { useEffect, useRef, useState } from 'react';
import { createChart, CandlestickSeries } from 'lightweight-charts';
import type { ChartResponse, TokenCardDto, QuotePayload } from '../types';
import { toTokenCardDto } from '../types';
import connection from '../lib/signalr';

interface Props {
  tokenId: string;
  onBack: () => void;
}

const intervals = ['5m', '15m', '1h'] as const;

export default function ChartPage({ tokenId, onBack }: Props) {
  const chartRef = useRef<HTMLDivElement>(null);
  const cleanupRef = useRef<(() => void) | null>(null);
  const [selectedInterval, setSelectedInterval] = useState<string>('5m');
  const [token, setToken] = useState<TokenCardDto | null>(null);

  useEffect(() => {
    fetch(`/api/v1/tokens/${tokenId}`)
      .then(res => res.json())
      .then(setToken);

    connection.on('token.quote', (p: QuotePayload | TokenCardDto) => {
      if ('token_id' in p && p.token_id === tokenId) {
        setToken(prev => toTokenCardDto(p, prev ?? undefined));
      } else if ('id' in p && p.id === tokenId) {
        setToken(p as TokenCardDto);
      }
    });

    return () => { connection.off('token.quote'); };
  }, [tokenId]);

  useEffect(() => {
    const from = new Date(Date.now() - 72 * 60 * 60 * 1000).toISOString();
    const to = new Date().toISOString();
    const el = chartRef.current;
    if (!el) return;

    fetch(`/api/v1/tokens/${tokenId}/chart?interval=${selectedInterval}&from=${from}&to=${to}`)
      .then(res => res.json())
      .then((data: ChartResponse) => {
        while (el.firstChild) el.removeChild(el.firstChild);

        const chart = createChart(el, {
          width: el.clientWidth,
          height: 500,
          layout: { background: { color: '#1f2937' }, textColor: '#9ca3af' },
          grid: { vertLines: { color: '#374151' }, horzLines: { color: '#374151' } },
          timeScale: { timeVisible: true },
        });
        cleanupRef.current = () => chart.remove();

        const series = chart.addSeries(CandlestickSeries, {
          upColor: '#22c55e',
          downColor: '#ef4444',
          borderUpColor: '#22c55e',
          borderDownColor: '#ef4444',
          wickUpColor: '#22c55e',
          wickDownColor: '#ef4444',
        });

        series.setData(data.candles.map(c => ({
          time: c.time.replace('Z', '').replace('T', ' ') as any,
          open: c.open,
          high: c.high,
          low: c.low,
          close: c.close,
        })));

        chart.timeScale().fitContent();
      });
  }, [tokenId, selectedInterval]);

  useEffect(() => () => cleanupRef.current?.(), []);

  return (
    <div className="p-4 max-w-5xl mx-auto">
      <button onClick={onBack} className="mb-4 px-3 py-1 bg-gray-800 rounded text-sm hover:bg-gray-700">← Back</button>

      {token && (
        <div className="mb-4">
          <h2 className="text-xl font-bold">{token.symbol}</h2>
          <div className="flex gap-4 text-sm text-gray-400">
            <span>Spread: <strong className="text-green-400">{token.spreadPct?.toFixed(2)}%</strong></span>
            <span>BingX: ${token.bingxAskPrice?.toFixed(8)}</span>
            <span>Jupiter: ${token.jupiterBuyPrice?.toFixed(8)}</span>
          </div>
        </div>
      )}

      <div className="flex gap-2 mb-2">
        {intervals.map(i => (
          <button key={i} onClick={() => setSelectedInterval(i)}
            className={`px-3 py-1 rounded text-sm ${selectedInterval === i ? 'bg-blue-600' : 'bg-gray-800 hover:bg-gray-700'}`}>{i}</button>
        ))}
      </div>

      <div ref={chartRef} />
    </div>
  );
}
