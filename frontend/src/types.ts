export interface UserTokenDto {
  id: string;
  symbol: string;
  name?: string;
  solanaMint: string;
  bingxSymbol: string;
  bingxUrl?: string;
  jupiterUrl?: string;
  solscanUrl?: string;
  bingxAskPrice: number;
  jupiterBuyPrice: number;
  spreadPct: number;
  telegramEnabled: boolean;
  isPinned: boolean;
  lastUpdated?: string;
  status?: string;
}

export interface BlacklistedTokenDto {
  id: string;
  symbol: string;
  name?: string;
  solanaMint: string;
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

export interface TickPoint {
  time: string;
  spreadPct: number;
  bingxPrice: number;
  jupiterPrice: number;
}
