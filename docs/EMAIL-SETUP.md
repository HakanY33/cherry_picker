# Mail Gönderim Kurulumu

Bu belge MIP Bilgi Teknolojileri içindir. Uygulama **mail ayarı olmadan da
çalışır**: bildirimler üretilir, kuyrukta bekler, uygulama içinde görünür.
Aşağıdaki ayarlar yapılınca aynı bildirimler e-posta olarak da gider.

Ayarların doğru okunduğunu görmek için: **Sistem → Mail Sağlığı** ekranı
(ADMIN rolü). Devreye alma sırasında bakılacak tek ekran orasıdır.

---

## 1. Gerekli ayarlar

`appsettings.json` içindeki `Email` bölümü:

| Ayar | Ne işe yarar | Örnek |
|---|---|---|
| `Enabled` | Ana anahtar. `false` iken hiçbir mail gönderilmez. | `true` |
| `Host` | SMTP sunucu adı | `smtp.mip.com.tr` |
| `Port` | SMTP portu | `587` |
| `UseStartTls` | STARTTLS ile şifreli bağlantı | `true` |
| `UserName` | SMTP kullanıcı adı. **Boş bırakılırsa** kimlik doğrulamasız (anonim relay) gönderilir. | `miprental` |
| `Password` | SMTP şifresi. **appsettings'e YAZILMAZ** → bkz. bölüm 2 | — |
| `FromAddress` | Gönderen adresi. İç/dış alıcı ayrımı da bu adresin alan adından türer. | `miprental@mip.com.tr` |
| `FromDisplayName` | Gönderen görünen adı | `MIP Hizmet Kiralama` |
| `AllowExternalRecipients` | `false` ise MIP alan adı dışına mail çıkmaz → bölüm 4 | `true` |
| `TestModeRecipient` | Dolu ise **tüm** mailler bu adrese gider → bölüm 3 | `test@mip.com.tr` |
| `QueueIntervalSeconds` | Kuyruk işleyicinin çalışma aralığı | `60` |
| `MaxRetryCount` | Bir bildirim için azami deneme | `5` |

`Enabled` true olsa bile `Host` veya `FromAddress` boşsa gönderim **açılmaz**;
uygulama sessizce kuyrukta biriktirmeye devam eder. Sağlık ekranı bu durumu
"Mail gönderimi KAPALI" olarak gösterir.

---

## 2. Şifre nereye konur

Şifre `appsettings.json` dosyasına **yazılmaz** (dosya sürüm kontrolündedir).

**Geliştirme — user-secrets:**

```
dotnet user-secrets set "Email:Password" "GERCEK_SIFRE" --project src/MipRental.Web
```

**Canlı — ortam değişkeni:**

```
setx ASPNETCORE_Email__Password "GERCEK_SIFRE" /M
```

İki alt çizgi (`__`) yapılandırmadaki `:` yerine geçer.

**Canlı — IIS uygulama ayarı:**
IIS Manager → site → *Configuration Editor* → `system.webServer/aspNetCore` →
`environmentVariables` → `Email__Password` eklenir. Uygulama havuzu yeniden
başlatılır.

Aynı yöntemle diğer ayarlar da ortamdan verilebilir
(`Email__Host`, `Email__Enabled`, ...); dosyayı değiştirmeye gerek yoktur.

---

## 3. Test modu

`TestModeRecipient` doluyken **tüm** mailler o adrese gider; gerçek alıcıya
hiçbir şey ulaşmaz. Bildirim kaydında gerçek alıcı olduğu gibi durur, sadece
teslim adresi değişir.

Devreye alma sırası önerisi:

1. `TestModeRecipient` = IT'den bir adres, `Enabled` = true
2. Sağlık ekranından **Test Maili Gönder** ile bağlantıyı doğrulayın
3. Sistemde birkaç gerçek bildirim üretin, hepsinin o adrese düştüğünü görün
4. `TestModeRecipient` boşaltılır — mailler artık gerçek alıcılara gider

---

## 4. Dış alıcı politikası

`AllowExternalRecipients = false` yapılırsa MIP alan adı **dışındaki** alıcılara
mail gönderilmez. Alan adı `FromAddress`ten türetilir: `miprental@mip.com.tr`
ise iç alan adı `mip.com.tr` olur.

Dışarıda kalan bildirimler kaybolmaz; **`SKIPPED_EXTERNAL`** olarak işaretlenir:

- hata sayılmaz, tekrar denenmez
- kayıt sağlık ekranında ve veritabanında görünmeye devam eder
- alt yüklenici (firma) kullanıcıları bildirimi uygulama içinden görür

Alt yüklenici adresleri genelde dış alan adındadır; bu politika kapatılırsa
firma tarafına mail gitmeyeceğini bilerek kapatın.

---

## 5. Sağlık kontrolü ekranı

**Sistem → Mail Sağlığı** (yalnızca ADMIN). Gösterdikleri:

- Gönderim açık mı, hangi sunucu/port, STARTTLS, gönderen adresi
- Şifrenin **tanımlı olup olmadığı** (şifrenin kendisi hiçbir şekilde gösterilmez)
- Kuyruk sayaçları: Kuyrukta / Gönderiliyor / Gönderildi / Başarısız / Dış alıcı atlandı
- Son 20 bildirim: alıcı, konu, durum, deneme sayısı, hata mesajı
- **Test Maili Gönder** — girilen adrese anında deneme maili (kuyruğa yazılmaz)

---

## 6. Kuyruk ve tekrar deneme

- Kuyruk işleyici her `QueueIntervalSeconds` saniyede bir çalışır
- Başarılı gönderim → `SENT` + zaman damgası
- Başarısız → `FAILED` yerine önce tekrar denenir: deneme sayısı artar, hata
  mesajı kaydedilir ve **üstel geri çekilme** uygulanır (1, 2, 4, 8 dakika)
- `MaxRetryCount` (varsayılan 5) dolduğunda kayıt `FAILED` kalır, bir daha
  denenmez. Sorun giderildikten sonra tekrar denenmesi isteniyorsa kaydın
  durumu veritabanından `QUEUED` yapılır.
- Aynı bildirim iki kez gönderilmez: satır tek bir atomik güncellemeyle
  üstlenilir (`SENDING`), ikinci işleyici o satırı alamaz.

---

## 7. Sık karşılaşılan hatalar

| Belirti | Olası sebep |
|---|---|
| Sağlık ekranı "KAPALI" diyor, ayarlar dolu | `Enabled` false ya da `Host`/`FromAddress` boş. Ortam değişkeni yazıldıysa uygulama havuzu yeniden başlatılmalı. |
| `The SMTP server requires a secure connection or the client was not authenticated` | `UserName`/`Password` eksik ya da yanlış; relay için sunucuda IP izni gerekebilir. |
| `Unable to read data from the transport connection` | Port veya STARTTLS ayarı yanlış (587/STARTTLS, 25/düz, 465 → bu sürümde desteklenmez). |
| `The operation has timed out` | Güvenlik duvarı SMTP portunu kapatıyor. |
| `Mailbox unavailable` / `Relay access denied` | Gönderen adresi sunucuda tanımlı değil ya da relay izni yok. |
| Mail gitmiyor ama hata da yok, durum `SKIPPED_EXTERNAL` | Dış alıcı politikası kapalı (bölüm 4). |
| Bildirim `QUEUED` kalıyor, deneme sayısı 0 | Gönderim kapalı; işleyici hiç çalışmıyor. Ayarları tamamlayın. |

---

## 8. Güvenlik notları

- Şifre yapılandırma dosyasında **tutulmaz**, ekranda **gösterilmez**, log'a
  **yazılmaz**.
- Hakediş onay maili tek kullanımlık bir bağlantı (magic link) taşır; bu mailin
  gövdesi hiçbir seviyede loglanmaz.
- Alıcı adresleri veritabanındaki kullanıcı kayıtlarından okunur. Adresin
  kullanıcı girdisinden geldiği tek yer sağlık ekranındaki deneme maili
  kutusudur ve o ekran ADMIN'e kapalıdır.
- Mail gövdelerinde **tutar bulunmaz** (fiyat gizliliği). Tek istisna, alıcısı
  zaten fiyat yetkilisi olan hakediş onay mailidir.
