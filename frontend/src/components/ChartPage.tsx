import { useEffect, useRef, useState } from 'react';
import { createChart, type IChartApi, type ISeriesApi, type UTCTimestamp, CandlestickSeries, LineSeries } from 'lightweight-charts';
import type { ChartResponse, UserTokenDto, QuotePayload } from '../types';
import { authFetch } from '../lib/auth';
import { on, off } from '../lib/sse';

interface Props {
  tokenId: string;
  onBack: () => void;
}

const intervals = ['5m', '15m', '1h'] as const;

// Must match ChartsController/SpreadTickRepository.GetChartAsync's bucketSeconds mapping.
const BUCKET_SECONDS: Record<string, number> = { '5m': 300, '15m': 900, '1h': 3600 };

// RetentionService hard-deletes spread_ticks/spread_candles older than this
// (see RetentionService.cs) — there is no data before it, ever, so once a
// history fetch hits this wall we stop asking instead of retrying forever.
const RETENTION_HOURS = 72;
const HISTORY_CHUNK_HOURS = 24;

interface Bar { time: UTCTimestamp; open: number; high: number; low: number; close: number; }

export default function ChartPage({ tokenId, onBack }: Props) {
  const candleRef = useRef<HTMLDivElement>(null);
  const lineRef = useRef<HTMLDivElement>(null);
  const candleChart = useRef<IChartApi | null>(null);
  const lineChart = useRef<IChartApi | null>(null);
  const candleSeries = useRef<ISeriesApi<'Candlestick'> | null>(null);
  const lineSeries = useRef<ISeriesApi<'Line'> | null>(null);
  const currentBar = useRef<Bar | null>(null);
  const allCandles = useRef<Bar[]>([]);
  const earliestLoaded = useRef<number | null>(null);
  const noMoreHistory = useRef(false);
  const loadingMoreHistory = useRef(false);
  const [selectedInterval, setSelectedInterval] = useState<string>('5m');
  const intervalRef = useRef(selectedInterval);
  const [token, setToken] = useState<UserTokenDto | null>(null);
  const [activeTab, setActiveTab] = useState<'candles' | 'spreadline'>('candles');

  useEffect(() => { intervalRef.current = selectedInterval; }, [selectedInterval]);

  useEffect(() => {
    authFetch(`/api/v1/tokens/${tokenId}`)
      .then(res => res.ok ? res.json() : null)
      .then((data: any) => {
        if (!data) return;
        setToken({
          id: data.id,
          symbol: data.symbol,
          bingxAskPrice: data.bingxAskPrice,
          jupiterBuyPrice: data.jupiterBuyPrice,
          spreadPct: data.spreadPct,
          lastUpdated: data.lastUpdated,
        } as UserTokenDto);
      });

    const onQuote = (p: QuotePayload) => {
      if (p.token_id !== tokenId) return;

      setToken(prev => prev ? {
        ...prev,
        bingxAskPrice: p.bingx_ask_price,
        jupiterBuyPrice: p.jupiter_buy_price,
        spreadPct: p.spread_pct,
        lastUpdated: p.calculated_at,
      } : prev);

      // Push straight into the chart series instead of React state — no
      // full re-render/rebuild per tick, which was making the chart look
      // like it kept resetting/rescaling on every update.
      if (lineSeries.current) {
        lineSeries.current.update({
          time: (new Date(p.calculated_at).getTime() / 1000) as UTCTimestamp,
          value: p.spread_pct,
        });
      }

      if (candleSeries.current) {
        const bucketSeconds = BUCKET_SECONDS[intervalRef.current] ?? 300;
        const nowSec = Math.floor(new Date(p.calculated_at).getTime() / 1000);
        const bucketStart = (Math.floor(nowSec / bucketSeconds) * bucketSeconds) as UTCTimestamp;

        const prevBar = currentBar.current;
        const bar: Bar = prevBar && prevBar.time === bucketStart
          ? { time: bucketStart, open: prevBar.open, high: Math.max(prevBar.high, p.spread_pct), low: Math.min(prevBar.low, p.spread_pct), close: p.spread_pct }
          : { time: bucketStart, open: p.spread_pct, high: p.spread_pct, low: p.spread_pct, close: p.spread_pct };

        currentBar.current = bar;
        candleSeries.current.update(bar);
      }
    };
    on('token.quote', onQuote);

    return () => { off('token.quote', onQuote); };
  }, [tokenId]);

  useEffect(() => {
    const el = candleRef.current;
    if (!el || activeTab !== 'candles') return;

    candleChart.current?.remove();
    candleSeries.current = null;
    currentBar.current = null;
    allCandles.current = [];
    earliestLoaded.current = null;
    noMoreHistory.current = false;
    loadingMoreHistory.current = false;

    const oldestPossible = Date.now() - RETENTION_HOURS * 60 * 60 * 1000;
    const from = new Date(oldestPossible).toISOString();
    const to = new Date().toISOString();

    const fetchChart = (fromIso: string, toIso: string) =>
      authFetch(`/api/v1/tokens/${tokenId}/chart?interval=${selectedInterval}&from=${fromIso}&to=${toIso}`)
        .then(res => res.ok ? res.json() : Promise.resolve({ candles: [] }))
        .then((data: Pick<ChartResponse, 'candles'>) => (data.candles ?? []).map(c => ({
          time: (new Date(c.time).getTime() / 1000) as UTCTimestamp,
          open: c.open, high: c.high, low: c.low, close: c.close,
        } as Bar)));

    const loadMoreHistory = async () => {
      if (loadingMoreHistory.current || noMoreHistory.current || earliestLoaded.current === null) return;
      if (earliestLoaded.current * 1000 <= oldestPossible) { noMoreHistory.current = true; return; }

      loadingMoreHistory.current = true;
      const chunkTo = new Date(earliestLoaded.current * 1000).toISOString();
      const chunkFrom = new Date(Math.max(oldestPossible, earliestLoaded.current * 1000 - HISTORY_CHUNK_HOURS * 60 * 60 * 1000)).toISOString();

      const older = await fetchChart(chunkFrom, chunkTo);
      if (older.length === 0) {
        noMoreHistory.current = chunkFrom === from || new Date(chunkFrom).getTime() <= oldestPossible;
      } else {
        allCandles.current = [...older, ...allCandles.current];
        earliestLoaded.current = allCandles.current[0].time;
        candleSeries.current?.setData(allCandles.current);
      }
      loadingMoreHistory.current = false;
    };

    fetchChart(from, to).then(candles => {
      while (el.firstChild) el.removeChild(el.firstChild);

      const chart = createChart(el, {
        autoSize: true,
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
      candleSeries.current = series;
      allCandles.current = candles;

      if (candles.length > 0) {
        series.setData(candles);
        currentBar.current = candles[candles.length - 1];
        earliestLoaded.current = candles[0].time;
        chart.timeScale().fitContent();
      } else {
        earliestLoaded.current = Math.floor(Date.now() / 1000) as UTCTimestamp;
      }

      chart.timeScale().subscribeVisibleLogicalRangeChange(range => {
        if (range && range.from < 5) loadMoreHistory();
      });
    });

    return () => {
      candleChart.current?.remove();
      candleChart.current = null;
      candleSeries.current = null;
    };
  }, [tokenId, selectedInterval, activeTab]);

  useEffect(() => {
    const el = lineRef.current;
    if (!el || activeTab !== 'spreadline') return;

    lineChart.current?.remove();
    lineSeries.current = null;
    while (el.firstChild) el.removeChild(el.firstChild);

    const chart = createChart(el, {
      autoSize: true,
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
    lineSeries.current = series;

    authFetch(`/api/v1/tokens/${tokenId}/ticks?limit=500`)
      .then(res => res.ok ? res.json() : [])
      .then((ticks: { time: string; spreadPct: number }[]) => {
        const points = ticks.map(t => ({
          time: (new Date(t.time).getTime() / 1000) as UTCTimestamp,
          value: t.spreadPct,
        })).sort((a, b) => a.time - b.time);

        if (points.length > 0) {
          series.setData(points);
          chart.timeScale().fitContent();
        }
      });

    return () => {
      lineChart.current?.remove();
      lineChart.current = null;
      lineSeries.current = null;
    };
  }, [tokenId, activeTab]);

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
