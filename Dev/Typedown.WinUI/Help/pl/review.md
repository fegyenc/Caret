# Recenzja: komentarze i zmiany

Recenzja działa jak narzędzia do recenzowania w edytorze tekstu, ale wszystko jest zapisane w dokumencie jako zwykły tekst, więc może to przeczytać także współpracownik, inny edytor i asystent AI. Znaczniki korzystają z publicznej konwencji o nazwie CriticMarkup:

| Co widzisz | Jak jest zapisane w pliku |
|---|---|
| zielony dodany tekst | `{++added++}` |
| czerwony usunięty tekst | `{--deleted--}` |
| zamiana | `{~~old~>new~~}` |
| żółty komentarz | `{==the text==}{>>@Name 2026-10-07: the note<<}` |

Nic nie jest przechowywane w innym miejscu, a recenzja nigdy nie włącza się sama: pisanie i usuwanie nie tworzą znaczników.

## Dodawanie komentarza

Zaznacz tekst, kliknij prawym przyciskiem myszy i wybierz **Dodaj komentarz** (albo **Recenzja > Dodaj komentarz**). Wpisz uwagę i sprawdź swoje imię (na początku jest to nazwa Twojego użytkownika w systemie Windows). Tekst zostanie podświetlony na żółto, a obok pojawi się uwaga. Jest wpisana w dokumencie, więc **Cofnij** działa.

## Porównanie z innym plikiem

**Recenzja > Porównaj z innym plikiem...** prosi o wcześniejszą wersję dokumentu (na przykład kopię, którą wysłano) i o to, kto wprowadził zmiany. Caret zapisuje różnice między tamtą wersją a dokumentem widocznym na ekranie jako recenzję *w nowej karcie*; Twój dokument pozostaje bez zmian. Użyj tego, aby zobaczyć, co współpracownik zmienił w zwróconym pliku. Jeśli karty są wyłączone (**Ustawienia > Karty i okna**), recenzja zastępuje Twój dokument w oknie, więc najpierw zapisz swój dokument.

## Śledzenie zmian podczas edycji

**Recenzja > Śledź zmiany** (także przycisk w panelu Recenzja) zapamiętuje dokument w obecnej postaci. Gdy edytujesz, chwilę po przerwaniu pisania Caret pokazuje, co zmieniłeś: prawy panel widoku **Podział** rysuje dokument z dodanymi fragmentami na zielono i usuniętymi na czerwono, każdy z Twoim imieniem i dniem, a panel Recenzja wymienia zmiany i je liczy (także na pasku stanu). Nic nie jest zapisywane w pliku. **Zatrzymaj śledzenie** zapomina wersję wyjściową i zostawia dokument bez zmian. Akceptowanie i odrzucanie pojedynczych zmian, widok Wizualny i zapisanie zmian w pliku jako recenzji to następne kroki; ten temat opisze je, gdy się pojawią.

## Akceptowanie i odrzucanie

- *Kliknij zmianę prawym przyciskiem myszy* (zieloną, czerwoną lub zamianę): **Zaakceptuj zmianę** zachowuje to, co zmiana mówi (dodania zostają, usunięcia znikają); **Odrzuć zmianę** przywraca stary tekst.
- *Kliknij komentarz prawym przyciskiem myszy:* **Usuń komentarz**.
- **Recenzja > Zaakceptuj wszystkie zmiany**, **Odrzuć wszystkie zmiany** i **Usuń wszystkie komentarze** robią to w całym dokumencie, jako jeden krok Cofnij.

## Ukrywanie znaczników

**Ustawienia > Edytor > Pokaż znaczniki recenzji** wyłącza kolory; znaczniki są wtedy widoczne jako zwykły tekst, którym w rzeczywistości są.

## Warto wiedzieć

- Znaczniki są tekstem, więc plik z recenzją można zapisać, wysłać i otworzyć w dowolnym edytorze.
- Zwrócony plik ze znacznikami współpracownika czyta się tak samo: jego zmiany są widoczne w kolorze, a Ty je akceptujesz lub odrzucasz.