# syntax=docker/dockerfile:1

# ---------------------------------------------------------------------------
# Étape de compilation du site.
# `npm ci` s'appuie sur le lockfile : les installations sont reproductibles.
# ---------------------------------------------------------------------------
FROM node:24-alpine AS build
WORKDIR /app

COPY package.json package-lock.json ./
RUN npm ci

COPY . .

# En production, le site et l'API sont servis derrière le même reverse proxy :
# les URL relatives suffisent, aucune origine n'a besoin d'être codée en dur.
ARG VITE_API_BASE_URL=""
ENV VITE_API_BASE_URL=$VITE_API_BASE_URL

RUN npm run build

# ---------------------------------------------------------------------------
# Image d'exécution : nginx sert les fichiers statiques et relaie /api vers l'API.
# ---------------------------------------------------------------------------
FROM nginx:1.27-alpine AS runtime

COPY --from=build /app/dist /usr/share/nginx/html
COPY nginx.conf /etc/nginx/conf.d/default.conf

EXPOSE 80

HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
    CMD wget --quiet --spider http://localhost/ || exit 1
