# Security policy

This repository is a portfolio demonstration. Do not commit real carrier, shipper, driver, customer, location, credential, token, or production-system data.

## Secrets

- Use environment variables or repository secrets for credentials.
- `ALVYS_CLIENT_ID`, `ALVYS_CLIENT_SECRET`, and `YARD_LTL_SIGNING_KEY` must never be committed with production values.
- Live third-party integrations are opt-in; demo mode is the default.

## Reporting

If you discover a security issue, open a private security advisory on the GitHub repository rather than a public issue.
