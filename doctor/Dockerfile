# syntax=docker/dockerfile:1
#
# Curatarr — "doctor" agent container (Option 1)
# ----------------------------------------------
# This is the AI/automation sidecar that can drive the Curatarr Docker
# stack. It:
#   - mounts the host Docker socket (so it can run docker / docker compose)
#   - mounts the codebase at /codebase (so it can edit files in-place)
#   - ships the Docker CLI + compose plugin + a few helpers
#
# It is NOT a Curatarr runtime service — it's the dev/ops agent that
# builds, runs, inspects and edits the Curatarr stack.

FROM docker:27-cli AS base

# docker:cli already includes the docker CLI; add the compose plugin and
# jq (handy for API / JSON inspection in scripts).
RUN apk add --no-cache docker-compose-plugin jq

WORKDIR /codebase

# Default shell for ad-hoc commands; override via docker run/compose.
ENTRYPOINT ["sh", "-c"]
CMD ["echo 'doctor container ready. use docker compose -f /codebase/Curatarr/docker-compose.yml ...' && tail -f /dev/null"]
