import type { TokenCardDto } from '../types';

interface Props {
  token: TokenCardDto;
  onClickChart: (id: string) => void;
}

function statusColor(status: string): string {
  switch (status) {
    case 'Active': return 'bg-green-500';
    case 'StaleBingx':
    case 'StaleJupiter': return 'bg-yellow-500';
    case 'ProxyError':
    case 'NoQuote': return 'bg-red-500';
    default: return 'bg-gray-500';
  }
}

export default function TokenCard({ token, onClickChart }: Props) {
  return (
    <div className="bg-gray-900 rounded-lg border border-gray-800 p-4 flex flex-col gap-2">
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-2">
          <span className={`w-2 h-2 rounded-full ${statusColor(token.status)}`} />
          <span className="font-semibold text-lg">{token.symbol}</span>
        </div>
        <span className="text-xl font-bold text-green-400">
          {token.spreadPct != null ? `${token.spreadPct.toFixed(2)}%` : '---'}
        </span>
      </div>

      <div className="grid grid-cols-2 gap-2 text-sm text-gray-400">
        <div>
          <span>BingX Ask: </span>
          <span className="text-white">${token.bingxAskPrice?.toFixed(8) ?? '---'}</span>
        </div>
        <div>
          <span>Jupiter Buy: </span>
          <span className="text-white">${token.jupiterBuyPrice?.toFixed(8) ?? '---'}</span>
        </div>
      </div>

      <div className="flex gap-2 mt-1">
        <a href={token.bingxUrl} target="_blank" rel="noreferrer"
          className="text-xs px-2 py-1 bg-blue-600 rounded hover:bg-blue-700">BINGX</a>
        <a href={token.jupiterUrl} target="_blank" rel="noreferrer"
          className="text-xs px-2 py-1 bg-purple-600 rounded hover:bg-purple-700">Jupiter</a>
        <a href={token.solscanUrl} target="_blank" rel="noreferrer"
          className="text-xs px-2 py-1 bg-gray-700 rounded hover:bg-gray-600">Contract</a>
        <button onClick={() => onClickChart(token.id)}
          className="text-xs px-2 py-1 bg-emerald-700 rounded hover:bg-emerald-600 ml-auto">Chart</button>
      </div>
    </div>
  );
}
