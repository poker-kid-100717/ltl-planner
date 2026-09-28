FROM node:24-alpine AS build
WORKDIR /app
COPY package.json .
RUN npm install
COPY . .
RUN npm run build

FROM nginx:1.29-alpine
ARG API_UPSTREAM=ltl-api:8080
COPY nginx.conf.template /etc/nginx/templates/default.conf.template
COPY --from=build /app/dist/web/browser /usr/share/nginx/html
ENV API_UPSTREAM=${API_UPSTREAM}
EXPOSE 80
