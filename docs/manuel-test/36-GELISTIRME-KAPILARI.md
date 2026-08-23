# 36 — Geliştirme Döngüsü Kapıları (`GDK`)

> **Alan kodu:** `GDK` · **Faz:** 91
> **Kaynak:** `scripts/kapi.py` · `scripts/denetim-paketi.py`
> · `scripts/*_test.py` · `src/AgentPrism.UI/AgentPrism.UI.Frontend.targets`

Bu aile, geliştirme kapılarının komutları sessizce atlamadığını ve tarihsel
kusur sınıflarını yeniden görebildiğini kanıtlar. Python testleri otomatik
kapıdır; aşağıdaki case'ler kabul davranışını tarif eder.

## Case'ler

| # | Kod | Ön koşul | Adımlar | Beklenen sonuç |
|---|---|---|---|---|
| 1 | `MT-GDK-001` | Temiz ağaç | `python3 scripts/kapi.py tarama` | Çıkış `0`; `Tarama: ✅ temiz` görünür |
| 2 | `MT-GDK-002` | `tests/` altında `Ornek 2.cs` dosyası | `python3 scripts/kapi.py tarama` | Çıkış `1`; dosya adı raporlanır. Dosya izlenmiş olsa da sonuç değişmez |
| 3 | `MT-GDK-003` | Temiz ağaç | `python3 scripts/kapi.py --komutlari-bas` | Hiçbir kapı koşmaz; dört .NET kapısı ve destek kapıları listelenir |
| 4 | `MT-GDK-004` | Derlenmiş `AgentPrism.Core.UnitTests` ikilisi | `python3 scripts/kapi.py test --sinif "*Capability*"` | Komut doğrudan test ikilisini ve `--filter-class` kullanır; `dotnet test --filter` kullanılmaz |
| 5 | `MT-GDK-005` | Git geçmişi erişilebilir | `python3 scripts/denetim-paketi.py --taban 7717ff1 --hedef 9b05f4b` | `RunEventWriter.cs` ve `Cost` imza-gövde adayı `ADAY` etiketiyle görünür; çıkış `0` |
| 6 | `MT-GDK-006` | Git geçmişi erişilebilir | Faz 68 ve Faz 73 SHA aralıklarını aynı komutla koş | Cache maliyeti ve iddiasız test adayları görünür; aday raporu kapıyı kırmaz |
| 7 | `MT-GDK-007` | Temiz ağaç | `python3 scripts/kapi.py kapanis --taban HEAD --site-atla` | Komutlar ucuzdan pahalıya koşar; ilk kırmızıdan sonra sonraki kapılar koşmaz |
| 8 | `MT-GDK-008` | Örnek uygulama build'i tamamlandı | Ardışık iki `dotnet build AgentPrism.slnx -c Release` koş | İkinci koşumda frontend kaynakları değişmediyse `npm run build` çalışmaz; `wwwroot` varlıkları yine pakete girer |

## Otomatik doğrulama

```bash
python3 -m unittest discover -s scripts -p "*_test.py"
python3 scripts/kapi.py tarama
python3 scripts/denetim-paketi.py --taban 7717ff1 --hedef 9b05f4b
```

`kapi.py`, komut sürelerini `artifacts/kapi-olcum.jsonl` dosyasına ekler.
`artifacts/` commit edilmez. `denetim-paketi.py` advisory'dir; ham diff'in
yerine geçmez ve regex adayları çıkış kodunu kırmaz.
