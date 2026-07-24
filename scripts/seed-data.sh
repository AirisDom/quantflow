#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(dirname "$SCRIPT_DIR")"

RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m'

log_info() { echo -e "${BLUE}[INFO]${NC} $1"; }
log_success() { echo -e "${GREEN}[SUCCESS]${NC} $1"; }
log_warn() { echo -e "${YELLOW}[WARN]${NC} $1"; }
log_error() { echo -e "${RED}[ERROR]${NC} $1"; }

CLEAR_EXISTING=false

while [[ $# -gt 0 ]]; do
    case $1 in
        --clear)
            CLEAR_EXISTING=true
            shift
            ;;
        --help|-h)
            echo "Usage: $0 [OPTIONS]"
            echo ""
            echo "Seeds the QuantFlow database with sample historical trades and portfolio data."
            echo ""
            echo "Options:"
            echo "  --clear    Clear existing data before seeding"
            echo "  --help     Show this help message"
            echo ""
            echo "Environment variables:"
            echo "  CONNECTION_STRING    PostgreSQL connection string"
            echo "                       Default: Host=localhost;Port=5432;Database=quantflow;Username=postgres;Password=postgres"
            exit 0
            ;;
        *)
            log_error "Unknown option: $1"
            exit 1
            ;;
    esac
done

echo ""
echo "=============================================="
echo "  QuantFlow Database Seeder"
echo "=============================================="
echo ""

if [[ ! -d "$PROJECT_ROOT/orchestrator" ]]; then
    log_error "Orchestrator project not found at $PROJECT_ROOT/orchestrator"
    exit 1
fi

cd "$PROJECT_ROOT/orchestrator"

log_info "Building orchestrator project..."
if ! dotnet build -c Release -q; then
    log_error "Build failed"
    exit 1
fi
log_success "Build completed"

CONNECTION_STRING="${CONNECTION_STRING:-Host=localhost;Port=5432;Database=quantflow;Username=postgres;Password=postgres}"

log_info "Applying database migrations..."
if ! dotnet ef database update --connection "$CONNECTION_STRING" 2>/dev/null; then
    log_warn "EF migrations failed - database may not be available or already up to date"
fi

log_info "Running database seeder..."
if [[ "$CLEAR_EXISTING" == "true" ]]; then
    log_warn "Clearing existing data before seeding"
    SEED_CLEAR="true"
else
    SEED_CLEAR="false"
fi

dotnet run --no-build -c Release -- --seed --clear-existing="$SEED_CLEAR" 2>&1 || {
    log_warn "Direct seeding not available, using fallback method..."

    SEED_SQL=$(cat <<'EOF'
DO $$
DECLARE
    assets TEXT[] := ARRAY['BTC/USD', 'ETH/USD', 'AAPL', 'GOOGL', 'MSFT', 'AMZN', 'TSLA', 'SPY'];
    base_prices DECIMAL[] := ARRAY[45000, 2800, 175, 140, 380, 170, 250, 450];
    asset_idx INT;
    trade_date TIMESTAMP;
    price DECIMAL;
    quantity DECIMAL;
    side TEXT;
    i INT;
    j INT;
BEGIN
    FOR i IN 0..89 LOOP
        FOR j IN 1..FLOOR(RANDOM() * 6 + 2)::INT LOOP
            asset_idx := FLOOR(RANDOM() * 8 + 1)::INT;
            trade_date := NOW() - (90 - i || ' days')::INTERVAL
                        + (FLOOR(RANDOM() * 7 + 9) || ' hours')::INTERVAL
                        + (FLOOR(RANDOM() * 60) || ' minutes')::INTERVAL;

            price := base_prices[asset_idx] * (1 + (RANDOM() - 0.5) * 0.1);

            IF asset_idx <= 2 THEN
                quantity := ROUND((RANDOM() * 0.5 + 0.01)::NUMERIC, 8);
            ELSE
                quantity := ROUND((RANDOM() * 50 + 1)::NUMERIC, 2);
            END IF;

            side := CASE WHEN RANDOM() > 0.5 THEN 'BUY' ELSE 'SELL' END;

            INSERT INTO "TradeRecords" ("Id", "OrderId", "Asset", "Side", "Quantity", "Price", "Timestamp")
            VALUES (
                gen_random_uuid(),
                'ORD-' || TO_CHAR(trade_date, 'YYYYMMDD') || '-' || UPPER(SUBSTRING(gen_random_uuid()::TEXT, 1, 8)),
                assets[asset_idx],
                side,
                quantity,
                ROUND(price::NUMERIC, CASE WHEN asset_idx <= 2 THEN 2 ELSE 4 END),
                trade_date
            );
        END LOOP;
    END LOOP;
END $$;
EOF
)

    if [[ "$CLEAR_EXISTING" == "true" ]]; then
        echo "DELETE FROM \"TradeRecords\";" | PGPASSWORD="${PGPASSWORD:-postgres}" psql -h "${PGHOST:-localhost}" -p "${PGPORT:-5432}" -U "${PGUSER:-postgres}" -d "${PGDATABASE:-quantflow}" -q 2>/dev/null || true
    fi

    echo "$SEED_SQL" | PGPASSWORD="${PGPASSWORD:-postgres}" psql -h "${PGHOST:-localhost}" -p "${PGPORT:-5432}" -U "${PGUSER:-postgres}" -d "${PGDATABASE:-quantflow}" -q 2>/dev/null || {
        log_error "Failed to seed database via SQL"
        log_info "Make sure PostgreSQL is running and accessible"
        exit 1
    }
}

log_success "Database seeding completed!"
echo ""

TRADE_COUNT=$(PGPASSWORD="${PGPASSWORD:-postgres}" psql -h "${PGHOST:-localhost}" -p "${PGPORT:-5432}" -U "${PGUSER:-postgres}" -d "${PGDATABASE:-quantflow}" -t -c "SELECT COUNT(*) FROM \"TradeRecords\"" 2>/dev/null || echo "unknown")

if [[ "$TRADE_COUNT" != "unknown" ]]; then
    log_info "Total trades in database: $(echo $TRADE_COUNT | xargs)"
fi

echo ""
echo "=============================================="
log_success "Seeding complete!"
echo "=============================================="
echo ""
echo "The database now contains:"
echo "  - Historical trades spanning 90 days"
echo "  - Multiple assets: BTC/USD, ETH/USD, AAPL, GOOGL, MSFT, AMZN, TSLA, SPY"
echo "  - Realistic price movements and trade volumes"
echo ""
echo "You can view the trades via the API:"
echo "  curl http://localhost:5000/trades"
echo ""
