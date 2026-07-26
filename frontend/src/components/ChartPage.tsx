import { useEffect, useRef, useState } from 'react';
import { createChart, type IChartApi, type ISeriesApi, type UTCTimestamp, type AutoscaleInfo, CandlestickSeries, LineSeries } from 'lightweight-charts';
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
interface LinePoint { time: UTCTimestamp; value: number; }

// A single wildly-off spread reading (bad token data, ticker collision,
// momentary glitch) stretches the whole price axis to fit it, squashing
// every normal candle into an unreadable sliver at one edge. Instead of
// naive min/max, clamp the visible range to the 2nd-98th percentile of the
// loaded values (+15% padding) so one outlier can't dominate the scale —
// it'll render partially or fully off-screen, which is the trade-off:
// readable normal candles instead of a flat line under one huge spike.
function percentile(sorted: number[], p: number): number {
  if (sorted.length === 0) return 0;
  const idx = Math.min(sorted.length - 1, Math.max(0, Math.round(p * (sorted.length - 1))));
  return sorted[idx];
}

function clampedRange(values: number[]): { minValue: number; maxValue: number } | null {
  if (values.length === 0) return null;
  const sorted = [...values].sort((a, b) => a - b);
  const lo = percentile(sorted, 0.02);
  const hi = percentile(sorted, 0.98);
  const pad = (hi - lo) * 0.15 || Math.abs(hi) * 0.1 || 1;
  return { minValue: lo - pad, maxValue: hi + pad };
}

// Backend only returns a candle for buckets that actually had ticks — gaps
// where nothing happened for a while otherwise render as empty space, which
// reads as "broken", not "no activity". Fill missing buckets with a flat
// (open=high=low=close) candle carrying the previous close forward, so the
// timeline is continuous like a real candlestick chart.
function fillGaps(bars: Bar[], bucketSeconds: number): Bar[] {
  if (bars.length === 0) return bars;
  const filled: Bar[] = [bars[0]];
  for (let i = 1; i < bars.length; i++) {
    let t = filled[filled.length - 1].time as number;
    const next = bars[i];
    while (t + bucketSeconds < next.time) {
      t += bucketSeconds;
      const prevClose = filled[filled.length - 1].close;
      filled.push({ time: t as UTCTimestamp, open: prevClose, high: prevClose, low: prevClose, close: prevClose });
    }
    filled.push(next);
  }
  return filled;
}

export default function ChartPage({ tokenId, onBack }: Props) {
  const candleRef = useRef<HTMLDivElement>(null);
  const lineRef = useRef<HTMLDivElement>(null);
  const candleChart = useRef<IChartApi | null>(null);
  const lineChart = useRef<IChartApi | null>(null);
  const candleSeries = useRef<ISeriesApi<'Candlestick'> | null>(null);
  const lineSeries = useRef<ISeriesApi<'Line'> | null>(null);
  const currentBar = useRef<Bar | null>(null);
  const allCandles = useRef<Bar[]>([]);
  const allLinePoints = useRef<LinePoint[]>([]);
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
        const point: LinePoint = {
          time: (new Date(p.calculated_at).getTime() / 1000) as UTCTimestamp,
          value: p.spread_pct,
        };
        lineSeries.current.update(point);
        allLinePoints.current.push(point);
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

        const arr = allCandles.current;
        if (arr.length > 0 && arr[arr.length - 1].time === bar.time) arr[arr.length - 1] = bar;
        else arr.push(bar);
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

    const bucketSeconds = BUCKET_SECONDS[selectedInterval] ?? 300;
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
        allCandles.current = fillGaps([...older, ...allCandles.current], bucketSeconds);
        earliestLoaded.current = allCandles.current[0].time;
        candleSeries.current?.setData(allCandles.current);
      }
      loadingMoreHistory.current = false;
    };

    fetchChart(from, to).then(rawCandles => {
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
        autoscaleInfoProvider: (original: () => AutoscaleInfo | null) => {
          const range = clampedRange(allCandles.current.flatMap(b => [b.low, b.high]));
          return range ? { priceRange: range } : original();
        },
      });
      candleSeries.current = series;

      const candles = fillGaps(rawCandles, bucketSeconds);
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
    allLinePoints.current = [];
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
      autoscaleInfoProvider: (original: () => AutoscaleInfo | null) => {
        const range = clampedRange(allLinePoints.current.map(p => p.value));
        return range ? { priceRange: range } : original();
      },
    });
    lineSeries.current = series;

    authFetch(`/api/v1/tokens/${tokenId}/ticks?limit=500`)
      .then(res => res.ok ? res.json() : [])
      .then((ticks: { time: string; spreadPct: number }[]) => {
        const points: LinePoint[] = ticks.map(t => ({
          time: (new Date(t.time).getTime() / 1000) as UTCTimestamp,
          value: t.spreadPct,
        })).sort((a, b) => a.time - b.time);

        allLinePoints.current = points;
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
