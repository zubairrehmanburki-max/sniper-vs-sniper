# One container hosts the whole game: the Colyseus server plus the built browser client.
FROM node:22-slim
RUN corepack enable
WORKDIR /app
COPY . .
RUN pnpm install --frozen-lockfile \
 && VITE_SERVER_URL=same-origin pnpm --filter @svs/client build
ENV NODE_ENV=production PORT=8080 CLIENT_DIR=/app/apps/client/dist
EXPOSE 8080
CMD ["pnpm", "--filter", "@svs/server", "start"]
