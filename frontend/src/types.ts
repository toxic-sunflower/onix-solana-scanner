export interface TokenCardDto {
  id: string;
  symbol: string;
  bingxAskPrice: number;
  jupiterBuyPrice: number;
  spreadPct: number;
  status: string;
  lastUpdated?: string;
  bingxUrl: string;
  jupiterUrl: string;
  solscanUrl: string;
}

export interface QuotePayload {
  version: number;
  event_id: number;
  token_id: string;
  symbol: string;
  bingx_ask_price: number;
  jupiter_buy_price: number;
  spread_pct: number;
  bingx_received_at?: string;
  jupiter_received_at?: string;
  calculated_at: string;
  status: string;
}

export interface StatusPayload {
  version: number;
  event_id: number;
  token_id: string;
  status: string;
  bingx_status: string;
  jupiter_status: string;
}

export function toTokenCardDto(p: QuotePayload, existing?: TokenCardDto): TokenCardDto {
  return {
    id: p.token_id,
    symbol: p.symbol,
    bingxAskPrice: p.bingx_ask_price,
    jupiterBuyPrice: p.jupiter_buy_price,
    spreadPct: p.spread_pct,
    status: p.status,
    lastUpdated: p.calculated_at,
    bingxUrl: existing?.bingxUrl ?? '',
    jupiterUrl: existing?.jupiterUrl ?? '',
    solscanUrl: existing?.solscanUrl ?? '',
  };
}

export interface ChartCandle {
  time: string;
  open: number;
  high: number;
  low: number;
  close: number;
  samples: number;
}

export interface ChartResponse {
  tokenId: string;
  interval: string;
  timezone: string;
  from: string;
  to: string;
  candles: ChartCandle[];
}
