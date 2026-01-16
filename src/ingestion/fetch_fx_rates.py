import requests
import psycopg2
from datetime import datetime, timedelta
from typing import List, Dict
import time

# Database config
DB_CONFIG = {
    "host": "localhost",
    "port": "5432",
    "database": "etfinsight",
    "user": "etfinsight",
    "password": "devpassword123",
}


def get_db_connection():
    """Establish a database connection"""
    conn = psycopg2.connect(**DB_CONFIG)
    return conn


def fetch_ecb_rates(currency_pair: str, start_date: str, end_date: str) -> List[Dict]:
    """
    Fetch FX rates from ECB API

    Args:
        currency_pair: e.g. "EUR/USD" or "EUR/GBP"
        start_date: 'YYYY-MM-DD'
        end_date: 'YYYY-MM-DD'

    Returns:
        List of rate records
    """

    # ECB uses format: EXR.D.USD.EUR.SP00.A (daily USD to EUR rate)
    # We'll fetch EUR as base (from_currency) to other currencies (to_currency)

    base_currency = "EUR"
    target_currency = currency_pair.split("/")[1]  # e.g. "USD" from "EUR/USD"

    # ECB statistical Data Warehouse API endpoint
    series_key = f"D.{target_currency}.{base_currency}.SP00.A"

    url = f"https://data-api.ecb.europa.eu/service/data/EXR/{series_key}"

    params = {"startPeriod": start_date, "endPeriod": end_date, "format": "jsondata"}

    print(f"Fetching FX rates for {currency_pair} from ECB API...")
    print(f" Period: {start_date} to {end_date}")

    try:
        response = requests.get(url, params=params, timeout=30)
        response.raise_for_status()

        data = response.json()

        # Parse ECB JSON structure
        observations = data.get("dataSets", [{}])[0].get("series", {})

        if not observations:
            print(f" No data found for {currency_pair}")
            return []

        # ECB returns data in complex nested structure
        # Get the first series (there should be only one for our query)
        series_key = list(observations.keys())[0]
        series_data = observations[series_key].get("observations", {})

        # Get dimension values (dates)
        dimensions = (
            data.get("structure", {}).get("dimensions", {}).get("observation", [])
        )
        date_dimension = next(
            (dim for dim in dimensions if dim.get("id") == "TIME_PERIOD"), None
        )

        if not date_dimension:
            print(f" No date dimension found in ECB data")
            return []

        dates = [v.get("id") for v in date_dimension.get("values", [])]

        # Build rate records
        rates = []
        for idx, rate_value in series_data.items():
            if int(idx) < len(dates):
                date = dates[int(idx)]
                rate = float(
                    rate_value[0]
                )  # Rate is the first element in the observation array

                rates.append(
                    {
                        "rate_date": date,
                        "from_currency": base_currency,
                        "to_currency": target_currency,
                        "rate": rate,
                    }
                )
        print(f" Fetched {len(rates)} records for {currency_pair}")
        return rates

    except requests.RequestException as e:
        print(f"Error fetching FX rates for {currency_pair}: {e}")
        return []
    except (KeyError, ValueError, IndexError) as e:
        print(f"Error parsing FX rates for {currency_pair}: {e}")
        return []


def calculate_cross_rate():
    """Calculate USD/GBP rate from EUR/USD and EUR/GBP rates"""

    # USD/GBP = (EUR/GBP) / (EUR/USD)
    query = """
        INSERT INTO fx_rates (rate_date, from_currency, to_currency, rate, source)
        SELECT
            eur_gbp.rate_date,
            'USD' AS from_currency,
            'GBP' AS to_currency,
            (eur_gbp.rate / eur_usd.rate) AS rate,
            'ECB_calculated' AS source
        FROM fx_rates AS eur_gbp
        JOIN fx_rates eur_usd ON eur_gbp.rate_date = eur_usd.rate_date
        WHERE 
            eur_gbp.from_currency = 'EUR' AND eur_gbp.to_currency = 'GBP'
            AND eur_usd.from_currency = 'EUR' AND eur_usd.to_currency = 'USD'
        ON CONFLICT (rate_date, from_currency, to_currency) DO NOTHING
    """

    # Also create inverse GBP/USD
    inverse_query = """
        INSERT INTO fx_rates (rate_date, from_currency, to_currency, rate, source)
        SELECT
            rate_date,
            'GBP' AS from_currency,
            'USD' AS to_currency,
            (1.0 / rate) AS rate,
            'ECB_calculated_inverse' AS source
        FROM fx_rates
        WHERE from_currency = 'USD' AND to_currency = 'GBP'
            AND source = 'ECB_calculated'
        ON CONFLICT (rate_date, from_currency, to_currency) DO NOTHING
    """

    conn = get_db_connection()
    cursor = conn.cursor()

    try:
        cursor.execute(query)
        cursor.execute(inverse_query)
        conn.commit()
    except Exception as e:
        print(f"Error calculating cross rates: {e}")
        conn.rollback()
    finally:
        cursor.close()
        conn.close()

    print("Calculated cross rates USD/GBP and GBP/USD.")


def insert_fx_rates(rates: List[Dict]) -> int:
    """
    Insert FX rates into the database

    Args:
        rates: List of rate records

    Returns:
        Number of inserted records
    """

    if not rates:
        return 0

    insert_query = """
        INSERT INTO fx_rates (rate_date, from_currency, to_currency, rate, source)
        VALUES (%s, %s, %s, %s, %s)
        ON CONFLICT (rate_date, from_currency, to_currency) DO NOTHING
    """

    conn = get_db_connection()
    cursor = conn.cursor()

    inserted = 0

    for rate in rates:
        try:
            cursor.execute(
                insert_query,
                (
                    rate["rate_date"],
                    rate["from_currency"],
                    rate["to_currency"],
                    rate["rate"],
                    "ECB",
                ),
            )
            if cursor.rowcount > 0:
                inserted += 1
        except Exception as e:
            print(f"  Error inserting rate for {rate['rate_date']}: {e}")
            conn.rollback()
            continue

    conn.commit()
    cursor.close()
    conn.close()

    return inserted


def main():
    """Fetch and store FX rates for major currency pairs"""

    # Define the date range: last 3 years
    end_date = datetime.now().date()
    start_date = end_date - timedelta(days=3 * 365)

    start_date_str = start_date.strftime("%Y-%m-%d")
    end_date_str = end_date.strftime("%Y-%m-%d")

    print(f"FX Rate Backfill")
    print(f"Period: {start_date_str} to {end_date_str}")
    print(f"Source: European Central Bank (ECB)\n")

    # Currency pairs to fetch (EUR as base)
    currency_pairs = [
        "EUR/USD",  # Euro to US Dollar
        "EUR/GBP",  # Euro to British Pound
        "USD/GBP",
    ]

    total_fetched = 0
    total_inserted = 0

    for pair in currency_pairs:
        print(f"Processing {pair}...")

        rates = fetch_ecb_rates(pair, start_date_str, end_date_str)

        if rates:
            inserted = insert_fx_rates(rates)
            total_fetched += len(rates)
            total_inserted += inserted
            print(
                f"  Inserted {inserted} new rates (duplicates skipped: {len(rates) - inserted})\n"
            )
        else:
            print(f" No rates to insert for {pair}\n")

        # Rate limiting: wait 3 seconds between requests
        time.sleep(3)

    calculate_cross_rate()

    print(f"Backfill complete:")
    print(f"  Total rates fetched: {total_fetched}")
    print(f"  New rates inserted: {total_inserted}")
    print(f"  Duplicates skipped: {total_fetched - total_inserted}")


if __name__ == "__main__":
    main()
