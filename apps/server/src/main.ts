import { createServer } from './server.ts';

const port = Number(process.env.PORT ?? 2567);
const server = createServer({ clientDir: process.env.CLIENT_DIR });
await server.listen(port, '0.0.0.0');
console.log(`Sniper vs Sniper server listening on port ${port}${process.env.CLIENT_DIR ? ', serving the client' : ''}`);
