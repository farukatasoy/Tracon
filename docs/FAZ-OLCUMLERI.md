# Faz Ölçümleri

> **Kapanan fazların süreç ölçümü.** Her fazın satırını
> `python3 scripts/dokuman-bakim.py faz-kapat <NN>` yazar. Elle yazılan tek
> hücre `Faz kapandıktan sonra bulunan kusur`dur (`kusur-giderme` Adım 6).
> Fazın kendi satırı: [`FAZ-DEFTERI.md`](FAZ-DEFTERI.md).

## Süreç Ölçümü

Eşik Faz 167'dir (K-767); öncesi ölçülmedi ve doldurulmaz. Kapı
satırın VARLIĞINI denetler, doğruluğunu değil (K-766). Açıklama fazın tam metnindedir.

| Faz | Plan revizyonu sayısı | Düzeltme turu sayısı | 🔴 bulgu: gerçek / gürültü / araştırılacak | Fazın ürettiği regresyon | Faz kapandıktan sonra bulunan kusur |
|---|---|---|---|---|---|
| 167 | 4 | 0 kod turu | 0 / 0 / 0 | 0. Denetçi iki kez ağaca/sürece müdahale etti | ölçülmedi |
| 168 | 0 | 2 | 2 / 0 / 0 | 0 | ölçülmedi |
| 169 | 5 | 4 | 1 / 0 / 0 | 0 | ölçülmedi |
| 170 | 0 | 2 | 2 / 0 / 0 | 0 | — |
| 171 | 0 | 9 | 1 / 0 / 0 | 0 | — |
| 172 | 0 | 1 | 0 / 0 / 0 | 0 | ölçülmedi |
| 173 | 0 | 9 | 2 / 0 / 0 | 0 | — |
| 174 | 5 | 5 | 7 / 0 / 0 | 0 | (kapanışta boş) |
| 175 | 0 | 3 | faz-denetim bölümüne bakınız | 1 | — |
| 176 | 0 | 4 | faz-denetim bölümüne bak | 0 | — |
| 177 | 0 | 4 | 2 / 0 / 0 | 0 | — |
| 178 | 0 | 3 | 2 / 0 / 0 + 1 bayat | 1 | — |
| 179 | 0 | 2 | 2 / 0 / 0 | 0 ölçülen. Kapılar üç kez kızardı ve üçü de fazın kendi eksiğiydi | 2 |
| 180 | 0 | 1 | 1 / 0 / 0 | 0 | — |
| 181 | 0 | 1 | 0 / 0 / 0 | 0 | — |
| 182 | 0 | 1 | 0 / 0 / 0 | 0 | 0 |
| 183 | 0 | 3 | 2 / 0 / 0 | 2 | ölçülmedi |
| 184 | 0 | 3 | 1 / 0 / 0 | 0 | ölçülmedi |
| 185 | 0 | 5 | 0 / 0 / 0 | 0 | ölçülmedi |
| 186 | 0 | 2 | 2 / 0 / 0 | 2 | ölçülmedi |
| 187 | 0 | 4 | 0 / 0 / 0 | 0 | ölçülmedi |
| 188 | 0 | 1 | 0 / 0 / 0 | 1 | ölçülmedi |
| 189 | 0 | 5 | 0 / 0 / 0 | 0 | ölçülmedi |
| 190 | 1 | 5 | 1 / 0 / 0 | 1 | ölçülmedi |
| 191 | 0 | 3 | 0 / 1 / 0 | 0 | ölçülmedi |
| 192 | 0 | 4 | 1 / 0 / 0 | 0 | 0 |
| 194 | 0 | 6 | 1 / 0 / 0 | 2 | 0 |
| 195 | 1 | 12 + kapanış kapıları | 1 / 1 / 0 | 2 | 0 |
| 196 | 2 | 1 | 0 / 0 / 0 | 1 | 1 |
| 197 | 1 | 12 | 1 / 0 / 0 | 3 | ölçülmedi |
| 198 | 1 | 3 | 0 / 0 / 0 | 1 | ölçülmedi |
| 199 | 0 | 2 | Denetçi | Davranış | Henüz ölçülmedi |
| 200 | 0 | 3 | 0 / 0 / 0 | 0 davranışta. Kapı turunda 2 bayat sayım | ölçülmedi |
| 201 | 0 | 10 | 0 / 0 / 0 | 0 | 0 |
| 202 | 1 | 1 | 0 / 0 / 0 | 1 | 0 |
| 203 | 1 | 1 | 0 / 0 / 0 | Merge'e giden | 0 |
| 204 | 0 | 6 | 0 / 0 / 0 | 0 | ölçülmedi |
| 205 | 0 | 9 | 1 / 0 / 0 | 0 | ölçülmedi |
| 206 | 0 | 3 | 0 / 0 / 0 | 0 | ölçülmedi |
| 207 | 0 | 3 | 0 / 0 / 0 | 0 | ölçülmedi |
| 208 | 2 | 2 | 2 / 0 / 0 | 0 | 0 |
| 209 | 0 | 1 | 0 / 0 / 0 | 0 | 0 |
| 210 | 0 | 1 | 0 / 0 / 0 | 0 | 0 |
| 211 | 0 | 3 | bkz. Denetim Bulguları | 0 | ölçülmedi |
| 212 | 0 | 4 | 0 / 0 / 0 | 1 | ölçülmedi |
| 213 | 0 | 5 | 1 / 0 / 0 | 0 commit edilmiş | ölçülmedi |
| 214 | 0 | 4 kapı kırmızısı turu | 0 / 0 / 0 | 0 bilinen | ölçülmedi |
| 215 | 0 | 2 | 1 / 1 / 0 | 1 | 0 |
| 216 | 0 | 1 | 0 / 0 / 0 | 0 | 0 |
| 217 | 1 | 3 | 2 / 0 / 0 | 2 | ölçülmedi |
| 218 | 0 | 3 | 0 / 0 / 0 | 2 | ölçülmedi |
| 219 | 0 | 2 | 0 / 0 / 0 | 0 commit edilmiş | ölçülmedi |
| 220 | 16 sapma | 2 | 0 / 0 / 0 | 0 bilinen | ölçülmedi |
| 221 | 8 | 1 | 2 / 0 / 0 | 0 | ölçülmedi |
| 222 | 0 | 2 | 1 / 0 / 1 | 1 | 6 |
| 223 | 9 | 1 | 0 / 1 / 0 | 0 | ölçülmedi |
| 224 | 1 | 1 | 0 / 0 / 0 | 0 | ölçülmedi |
| 225 | 0 | 3 | 2 / 0 / 0 | 1 | ölçülmedi |
| 226 | 12 | 3 denetim sonrası | 1 / 0 / 0 | 0. Hiçbir şey denetimden önce commit edilmedi | ölçülmedi |
| 227 | 0 | 3 | 0 / 0 / 0 | 1 | ölçülmedi |
| 228 | 8 sapma | 2 | 0 / 0 / 0 | 0 bilinen | ölçülmedi |
| 229 | 12 | 1 | 1 / 0 / 0 | 0 | ölçülmedi |
| 230 | 10 | 3 | 2 / 0 / 0 | 12 test | ölçülmedi |
| 231 | 0 yeniden yazım | 3 | 0 / 0 / 0 | 0 kullanıcıya ulaşan. Kapanıştan önce 3 ratchet düştü | ölçülmedi |
| 232 | 0 | 1 | 2 / 0 / 0 | 3 | ölçülmedi |
| 233 | 4 | 2 | 1 / 0 / 0 | 0 | ölçülmedi |
| 234 | 5 | 2 | 0 / 0 / 0 | 2 | ölçülmedi |
| 235 | 3 | 1 | 1 / 0 / 0 | 1 | ölçülmedi |
| 236 | 4 | 1 | 1 / 0 / 0 | 1 | ölçülmedi |
| 237 | 0 | 2 | 1 / 0 / 0 | 1 | ölçülmedi |
| 238 | 14 | 1 | 2 / 0 / 0 | 0 | ölçülmedi |
| 239 | 0 | 1 | 1 / 0 / 0 | 0 | ölçülmedi |
| 240 | 0 | 1 | 2 / 0 / 0 | 1 | ölçülmedi |
| 241 | 0 | 3 | 0 / 0 / 0 | 0 | ölçülmedi |
| 242 | 0 | 4 | 0 / 0 / 0 | 0 | ölçülmedi |
| 243 | 0 | 1 | 0 / 0 / 0 | 0 | ölçülmedi |
| 244 | 9 sapma | 1 | 2 / 0 / 0 | 0 | ölçülmedi |
| 245 | 12 sapma | 1 | 1 / 0 / 0 | 0 | ölçülmedi |
| 246 | 0 | 1 | 0 / 0 / 1 | 0 | ölçülmedi |
| 247 | 13 sapma | 2 | 0 / 0 / 0 | 0 | ölçülmedi |
| 248 | 9 sapma | 2 | 1 / 0 / 0 | 0 | ölçülmedi |
| 249 | 0 | 1 | 1 / 0 / 0 | 0 | ölçülmedi |
| 250 | 0 | 1 | 0 / 0 / 0 | 0 bilinen | ölçülmedi |
| 251 | 12 sapma | 1 | 0 / 0 / 0 | 0 | ölçülmedi |
| 252 | 13 sapma | 1 | 0 / 0 / 0 | 0 | ölçülmedi |
| 253 | 9 sapma | 1 | 0 / 0 / 0 | 0 | ölçülmedi |
| 254 | 17 sapma | 1 | 0 / 0 / 0 | 0 | ölçülmedi |
| 255 | 0 | 1 | 2 / 0 / 0 | 1 | ölçülmedi |
| 256 | 0 | 3 | 0 / 0 / 0 | 1 | ölçülmedi |
