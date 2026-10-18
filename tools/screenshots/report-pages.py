# Rendert ausgewählte Seiten eines Zutritt-PDF-Berichts als PNG für die Doku.
# Aufruf: python report-pages.py <bericht.pdf> <docs/assets-Ordner>
# Gesucht wird nach Überschriften statt fester Seitennummern, damit es bei anderem Umfang weiter passt.
# Braucht pypdfium2 und Pillow (pip install pypdfium2 pillow) – beide mit freizügiger Lizenz
# (pypdfium2: Apache-2.0 oder BSD-3-Clause, Pillow: MIT-CMU), anders als PyMuPDF (AGPL).
import os
import sys

import pypdfium2 as pdfium

DPI = 110

pdf, assets = sys.argv[1], sys.argv[2]
doc = pdfium.PdfDocument(pdf)


def text(index):
    page = doc[index]
    textpage = page.get_textpage()
    try:
        return textpage.get_text_range()
    finally:
        textpage.close()
        page.close()


def find(title, last=False):
    # Ab Seite 2 suchen: das Deckblatt nennt alle Abschnitte im Inhaltsverzeichnis
    order = range(len(doc) - 1, 0, -1) if last else range(1, len(doc))
    for i in order:
        if title in text(i):
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
    page = doc[index]
    try:
        page.render(scale=DPI / 72).to_pil().save(os.path.join(assets, f'{name}.png'))
    finally:
        page.close()
    print(f'{name}.png: Seite {index + 1}')
doc.close()
