import * as signalR from '@microsoft/signalr';

const connection = new signalR.HubConnectionBuilder()
  .withUrl('/hubs/spread')
  .withAutomaticReconnect()
  .build();

export async function startConnection() {
  try {
    await connection.start();
    console.log('SignalR connected');
  } catch (err) {
    console.error('SignalR connection failed', err);
  }
}

export default connection;
