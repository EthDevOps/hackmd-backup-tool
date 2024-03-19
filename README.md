# HackMD Backup tool

This tool will utilize the (inofficial) HackMD Admin API to download all notes, pack them into a tar.gz and encrypt that file with a GPG key.
It will then upload that buckup to S3 compatible storage.

## Configuration

Configuration is done via environment variables

- `HEALTHCHECK_URL` - URL to ping once the process completes. Uses the PingAPI from Healthchecks.io
- `GPG_PUBKEY_FILE` - GPG public key file in `.asc` format (armored ASCII)
- `BACKUP_TAR_PATH` (optional) - Path to the temorarily generated tar file. The encrypted file will be generated with the smae basename in the same directory/ 
- `WEBDRIVER_URL` (optional) - Selenium WebDriver URL to connect to a Selenium grid engine.
- `S3_HOST` - S3-compatible host to send the encrypted tar file to
- `S3_ACCESS_KEY` - S3 Access key
- `S3_SECRET_KEY` - S3 secret key
- `S3_BUCKET` - S3 bucket
- `GITHUB_USERNAME` - GitHub username for OAuth authentication against HackMD
- `GITHUB_PASSWORD` - GitHub password (not an access token!)
- `GITHUB_OTP_SEED` - GitHub TOTP seed/secret
- `BACKUP_PATH` - Path to the working directory
