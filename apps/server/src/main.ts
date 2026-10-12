import { createServer } from './server.ts';

const port = Number(process.env.PORT ?? 2567);
const server = createServer();
await server.listen(port);
console.log(`Sniper vs Sniper server listening on ws://localhost:${port}`);
