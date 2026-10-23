FROM node:24-bookworm-slim AS build
WORKDIR /src
COPY client/tibiaintel-angular/package*.json ./
RUN npm ci
COPY client/tibiaintel-angular/ ./
RUN npm run build

FROM nginx:stable-alpine
COPY deploy/web.conf /etc/nginx/conf.d/default.conf
COPY --from=build /src/dist/tibiaintel-angular/browser /usr/share/nginx/html
EXPOSE 80
