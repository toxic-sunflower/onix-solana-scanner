import { useEffect, useRef, useState } from 'react';
import { createChart, type IChartApi, CandlestickSeries, LineSeries } from 'lightweight-charts';
import type { ChartResponse, UserTokenDto, QuotePayload, TickPoint } from '../types';
import { on, off } from '../lib/sse';

interface Props {
  tokenId: string;
  onBack: () => void;
}

const intervals = ['5m', '15m', '1h'] as const;

export default function ChartPage({ tokenId, onBack }: Props) {
  const candleRef = useRef<HTMLDivElement>(null);
  const lineRef = useRef<HTMLDivElement>(null);
  const candleChart = useRef<IChartApi | null>(null);
  const lineChart = useRef<IChartApi | null>(null);
  const [selectedInterval, setSelectedInterval] = useState<string>('5m');
  const [token, setToken] = useState<UserTokenDto | null>(null);
  const [ticks, setTicks] = useState<TickPoint[]>([]);
  const [activeTab, setActiveTab] = useState<'candles' | 'spreadline'>('candles');

  useEffect(() => {
    fetch(`/api/v1/tokens/${tokenId}`)
      .then(res => res.json())
      .then((data: any) => {
        setToken({
          id: data.id,
          symbol: data.symbol,
          bingxAskPrice: data.bingxAskPrice,
          jupiterBuyPrice: data.jupiterBuyPrice,
          spreadPct: data.spreadPct,
          lastUpdated: data.lastUpdated,
        } as UserTokenDto);
      });

    fetch(`/api/v1/tokens/${tokenId}/ticks?limit=500`)
      .then(res => res.ok ? res.json() : [])
      .then(setTicks);

    const onQuote = (p: QuotePayload) => {
      if (p.token_id === tokenId) {
        setToken(prev => prev ? {
          ...prev,
          bingxAskPrice: p.bingx_ask_price,
          jupiterBuyPrice: p.jupiter_buy_price,
          spreadPct: p.spread_pct,
          lastUpdated: p.calculated_at,
        } : prev);
      }
    };
    on('token.quote', onQuote);

    return () => { off('token.quote', onQuote); };
  }, [tokenId]);

  useEffect(() => {
    const el = candleRef.current;
    if (!el || activeTab !== 'candles') return;

    candleChart.current?.remove();
    const from = new Date(Date.now() - 72 * 60 * 60 * 1000).toISOString();
    const to = new Date().toISOString();

    fetch(`/api/v1/tokens/${tokenId}/chart?interval=${selectedInterval}&from=${from}&to=${to}`)
      .then(res => res.json())
      .then((data: ChartResponse) => {
        while (el.firstChild) el.removeChild(el.firstChild);

        const chart = createChart(el, {
          width: el.clientWidth,
          height: 400,
          layout: { background: { color: '#1a1b24' }, textColor: '#9ca3af' },
          grid: { vertLines: { color: '#2a2b36' }, horzLines: { color: '#2a2b36' } },
          timeScale: { timeVisible: true, borderColor: '#374151' },
          rightPriceScale: { borderColor: '#374151' },
        });
        candleChart.current = chart;

        const series = chart.addSeries(CandlestickSeries, {
          upColor: '#22c55e',
          downColor: '#ef4444',
          borderUpColor: '#22c55e',
          borderDownColor: '#ef4444',
          wickUpColor: '#22c55e',
          wickDownColor: '#ef4444',
        });

        const candles = (data.candles ?? []).map(c => ({
          time: new Date(c.time).getTime() / 1000 as any,
          open: c.open, high: c.high, low: c.low, close: c.close,
        }));

        if (candles.length > 0) {
          series.setData(candles);
          chart.timeScale().fitContent();
        }
      });
  }, [tokenId, selectedInterval, activeTab]);

  useEffect(() => {
    const el = lineRef.current;
    if (!el || activeTab !== 'spreadline') return;

    lineChart.current?.remove();
    while (el.firstChild) el.removeChild(el.firstChild);

    const w = el.clientWidth || el.parentElement?.clientWidth || 600;
    const chart = createChart(el, {
      width: w,
      height: 200,
      layout: { background: { color: '#1a1b24' }, textColor: '#9ca3af' },
      grid: { vertLines: { color: '#2a2b36' }, horzLines: { color: '#2a2b36' } },
      timeScale: { timeVisible: true, borderColor: '#374151' },
      rightPriceScale: { borderColor: '#374151' },
    });
    lineChart.current = chart;

    const series = chart.addSeries(LineSeries, {
      color: '#f59e0b',
      lineWidth: 2,
      crosshairMarkerVisible: true,
    });

    const points = ticks.map(t => ({
      time: new Date(t.time).getTime() / 1000 as any,
      value: t.spreadPct,
    }));

    if (points.length > 0) {
      series.setData(points);
      chart.timeScale().fitContent();
    }
  }, [ticks, activeTab]);

  useEffect(() => {
    const ro = new ResizeObserver(() => {
      candleChart.current?.resize(candleRef.current?.clientWidth ?? 600, 400);
      lineChart.current?.resize(lineRef.current?.clientWidth ?? 600, 200);
    });
    if (candleRef.current) ro.observe(candleRef.current);
    if (lineRef.current) ro.observe(lineRef.current);
    return () => ro.disconnect();
  }, []);

  useEffect(() => () => {
    candleChart.current?.remove();
    lineChart.current?.remove();
  }, []);

  return (
    <div className="p-4 max-w-5xl mx-auto">
      <button onClick={onBack} className="mb-4 px-3 py-1 bg-[#1e1f28] rounded text-sm text-[#94a3b8] hover:text-[#f59e0b] transition-colors">← Dashboard</button>

      {token && (
        <div className="mb-4">
          <h2 className="text-xl font-bold text-[#f1f5f9]">{token.symbol}</h2>
          <div className="flex gap-4 text-sm text-[#94a3b8]">
            <span>Spread: <strong className={token.spreadPct >= 0 ? 'text-[#22c55e]' : 'text-[#ef4444]'}>{token.spreadPct?.toFixed(4)}%</strong></span>
            <span>CEX: <strong className="text-[#f1f5f9]">${token.bingxAskPrice?.toFixed(6)}</strong></span>
            <span>DEX: <strong className="text-[#f1f5f9]">${token.jupiterBuyPrice?.toFixed(6)}</strong></span>
          </div>
        </div>
      )}

      <div className="flex gap-2 mb-3 flex-wrap items-center">
        <button onClick={() => setActiveTab('candles')}
          className={`px-3 py-1 rounded text-sm ${activeTab === 'candles' ? 'bg-[#d97706] text-black' : 'bg-[#1e1f28] text-[#94a3b8] hover:text-[#f1f5f9]'}`}>Candles</button>
        <button onClick={() => setActiveTab('spreadline')}
          className={`px-3 py-1 rounded text-sm ${activeTab === 'spreadline' ? 'bg-[#d97706] text-black' : 'bg-[#1e1f28] text-[#94a3b8] hover:text-[#f1f5f9]'}`}>Spread line</button>
        {activeTab === 'candles' && intervals.map(i => (
          <button key={i} onClick={() => setSelectedInterval(i)}
            className={`px-3 py-1 rounded text-sm ${selectedInterval === i ? 'bg-[#1e1f28] text-[#f1f5f9]' : 'bg-transparent text-[#64748b] hover:text-[#94a3b8]'}`}>{i}</button>
        ))}
      </div>

      <div ref={candleRef} className={activeTab === 'candles' ? '' : 'hidden'} />
      <div ref={lineRef} className={activeTab === 'spreadline' ? '' : 'hidden'} />
    </div>
  );
}
