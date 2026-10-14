# Rendert ausgewählte Seiten eines Zutritt-PDF-Berichts als PNG für die Doku.
# Aufruf: python report-pages.py <bericht.pdf> <docs/assets-Ordner>
# Gesucht wird nach Überschriften statt fester Seitennummern, damit es bei anderem Umfang weiter passt.
import os
import sys

import fitz  # PyMuPDF

pdf, assets = sys.argv[1], sys.argv[2]
doc = fitz.open(pdf)


def find(text, last=False):
    # Ab Seite 2 suchen: das Deckblatt nennt alle Abschnitte im Inhaltsverzeichnis
    order = range(doc.page_count - 1, 0, -1) if last else range(1, doc.page_count)
    for i in order:
        if text in doc[i].get_text():
            return i
    return None


pages = {
    'report-cover': 0,
    'report-summary': find('Zusammenfassung'),
    'report-changes': find('Änderungen seit dem letzten Lauf') or find('Vergleich mit dem Lauf vom'),
    'report-signoff': find('Prüfung und Freigabe', last=True),
}
for name, index in pages.items():
    if index is None:
        print(f'{name}: Seite nicht gefunden, übersprungen')
        continue
    doc[index].get_pixmap(dpi=110).save(os.path.join(assets, f'{name}.png'))
    print(f'{name}.png: Seite {index + 1}')
