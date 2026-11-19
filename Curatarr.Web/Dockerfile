# syntax=docker/dockerfile:1
#
# Curatarr Web — Docker image
# ---------------------------
# Build context is the REPO ROOT (per docker-compose.yml `context: .`),
# so all Stage 1 COPY sources are prefixed with Curatarr.Web/.
#
# Stage 1 builds the Vite/React SPA. The app uses RELATIVE API paths
# (/api/*), so there is no build-time VITE_ env var and the resulting
# bundle is server-agnostic — one image works behind any proxy.
#
# Running the app is deliberately NOT Node: the SPA is static files, so
# nginx serves it and reverse-proxies /api + /hubs to the API container.
# Client-side routes (accept-invite, reset-password, confirm-email-change)
# are handled with try_files -> index.html so a hard refresh works.

# ---- Stage 1: build the SPA ----------------------------------------------
FROM node:22-alpine AS build
WORKDIR /app

# package-lock.json exists, so npm ci gives a reproducible install
# NOTE: with repo-root build context, the npm manifests live in Curatarr.Web/
COPY Curatarr.Web/package.json Curatarr.Web/package-lock.json ./
RUN npm ci

# NOTE: with repo-root build context, copy the Web project dir, not the whole repo
COPY Curatarr.Web/. .
# Emits the production bundle to dist/
RUN npm run build

# ---- Stage 2: nginx runtime ----------------------------------------------
FROM nginx:1.27-alpine AS runtime

# Replace the default site with our SPA + API proxy config
# NOTE: build context is the repo root (per docker-compose.yml `context: .`),
# so the COPY source must be prefixed with Curatarr.Web/. The site conf
# (server-block only) lives in Curatarr.Web/nginx.conf; the root nginx.conf
# is a full main conf and must NOT go into conf.d (events/http are illegal there).
COPY Curatarr.Web/nginx.conf /etc/nginx/conf.d/default.conf

# Copy the built SPA into nginx's html root
COPY --from=build /app/dist /usr/share/nginx/html

EXPOSE 80
# default nginx entrypoint is fine; no CMD needed