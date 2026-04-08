import re

text = (
    "TRADE REPUBLIC BANK GMBH, BRANCH ITALY SPACES GAE AULENTI, PIAZZA GAE AULENTI 1, TORRE B 20154 MILANO (MI)\n"
    "Simone Riggi PAGINA 1 da 1\n"
    "Viale della Rinascita 4B DATA 02.03.2026\n"
    "93017 San Cataldo ESECUZIONE 100c-2d49\n"
    "PIANO DI ACCUMULO c57a-f6c2\n"
    "CONTO TITOLI 3079293601\n"
    "PIANO D'INVESTIMENTO PER IL REGOLAMENTO DEI TITOLI\n"
    "PANORAMICA\n"
    "Esecuzione del piano d'accumulo il 02.03.2026 su Lang und Schwarz Exchange.\n"
    "La controparte dell'operazione \xe8 Lang & Schwarz TradeCenter AG & Co. KG.\n"
    "POSIZIONE QUANTIT\u00c0 PREZZO MEDIO IMPORTO\n"
    "Core MSCI World USD (Acc) 7,378349 113,8466 EUR 840,00 EUR\n"
    "ISIN: IE00B4L5Y983\n"
    "TOTALE 840,00 EUR\n"
    "PRENOTAZIONE\n"
    "CONTO DI TRANSITO DATA VALUTA IMPORTO\n"
    "IT83A0367401600003079293611 2026-03-04 -840,00 EUR\n"
    "Core MSCI World USD (Acc) custodia sicura non collettiva"
)

flags = re.IGNORECASE | re.MULTILINE

patterns = {
    "ISIN": r"ISIN:\s*([A-Z]{2}[A-Z0-9]{9}\d)",
    "ESECUZIONE ref": r"ESECUZIONE\s+([A-Za-z0-9\-]+)",
    "PIANO DI ACCUMULO": r"PIANO DI ACCUMULO\s+([A-Za-z0-9\-]+)",
    "QUANTITA (plan)": r"QUANTIT[A\u00c0]\s+([\d]+[,.][\d]+)",
    "PREZZO MEDIO (plan)": r"PREZZO MEDIO\s+([\d]+[,.][\d]+)\s+([A-Z]{3})",
    "TOTALE": r"TOTALE\s+([\d]+[,.][\d]+)\s+([A-Z]{3})",
    "Settlement date (plan)": r"DATA VALUTA\s+(\d{2}\.\d{2}\.\d{4})",
    "Txn date (plan label)": r"DATA (?:DI ESECUZIONE|OPERAZIONE)\s+(\d{2}\.\d{2}\.\d{4})",
    "Txn date (simple DATA)": r"\bDATA\s+(\d{2}\.\d{2}\.\d{4})",
    "Settlement date (iso)": r"DATA VALUTA[\s\S]*?(\d{4}-\d{2}-\d{2})",
    "Table row (name+units+price+gross)": r"^(.+?)\s+([\d]+,[\d]+)\s+([\d]+,[\d]+)\s+EUR\s+([\d]+,[\d]+)\s+EUR$",
}

for name, p in patterns.items():
    m = re.search(p, text, flags)
    if m:
        print(f"  OK  {name}: {m.groups()}")
    else:
        print(f"  FAIL {name}: NO MATCH")
