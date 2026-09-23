# The customer website's renderer (Angular SSR on Node).
#
#     docker build -f Khadra.Web/Dockerfile -t khadra-web .
#
# It serves pages only. It is meant to sit BEHIND the customer BFF (Khadra.Bff with
# BffSecurity__Deployment=customer-web), which owns the public host, the session and /api, and
# forwards every other path here. Nothing about a host is baked into the image:
#
#   KHADRA_API_URL          where the renderer reaches the API (private network when there is one)
#   KHADRA_PUBLIC_BASE_URL  the public origin, for canonical URLs and the sitemap
#   KHADRA_EDGE_SECRET      the customer BFF's BffSecurity__FrontendSharedSecret (a secret)
#   NG_ALLOWED_HOSTS        the Host header the BFF forwards with (the renderer's own service name)
#   PORT                    default 4000

FROM node:22-bookworm-slim AS build
WORKDIR /web
COPY Khadra.Web/package.json Khadra.Web/package-lock.json ./
RUN npm ci
COPY Khadra.Web/ ./
RUN npx ng build --configuration production

FROM node:22-bookworm-slim AS final
WORKDIR /app
ENV NODE_ENV=production PORT=4000
COPY --from=build /web/dist/khadra-web ./dist/khadra-web
USER node
EXPOSE 4000
CMD ["node", "dist/khadra-web/server/server.mjs"]
