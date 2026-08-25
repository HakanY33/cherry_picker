/* =========================================================================
   MIP - Hizmet & Kiralama Yonetim Sistemi
   Veritabani Iskeleti (SQL Server / T-SQL)
   Faz 1: Cherry Picker | Faz 2: Genel hizmet kiralama + Oracle entegrasyonu

   Tasarim ilkeleri:
   - Sema sabit, veri degisken. Yeni hizmet = yeni satir, yeni tablo degil.
   - Fiyat ve kurallar parametrik ve tarih araligi versiyonlu.
   - Hicbir mali kayit silinmez; duzeltme = yeni versiyon + gerekce.
   - Onay zinciri kodda degil tabloda tanimli.
   ========================================================================= */

/* -------------------------------------------------------------------------
   1. ANA VERILER (MASTER DATA)
   ------------------------------------------------------------------------- */

-- Alt yukleniciler / hizmet veren firmalar
CREATE TABLE Firms (
    FirmId              INT IDENTITY(1,1) PRIMARY KEY,
    Code                NVARCHAR(20)   NOT NULL UNIQUE,   -- kisa kod (CELIKVINC)
    Title               NVARCHAR(200)  NOT NULL,          -- resmi unvan
    TaxNumber           NVARCHAR(20)   NULL,
    TaxOffice           NVARCHAR(100)  NULL,
    Iban                NVARCHAR(34)   NULL,
    Phone               NVARCHAR(30)   NULL,
    Email               NVARCHAR(150)  NULL,
    Address             NVARCHAR(500)  NULL,
    IsActive            BIT            NOT NULL DEFAULT 1,
    CreatedAt           DATETIME2      NOT NULL DEFAULT SYSUTCDATETIME(),
    CreatedBy           INT            NULL
);

-- MIP departmanlari
CREATE TABLE Departments (
    DepartmentId        INT IDENTITY(1,1) PRIMARY KEY,
    Code                NVARCHAR(20)   NOT NULL UNIQUE,
    Name                NVARCHAR(150)  NOT NULL,          -- Ekipman Mudurlugu
    ParentDepartmentId  INT            NULL REFERENCES Departments(DepartmentId),
    IsActive            BIT            NOT NULL DEFAULT 1
);

-- Kullanicilar. MIP personeli AD/Entra ile eslesir (FirmId NULL).
-- Firma kullanicilarinin FirmId'si dolu olur.
CREATE TABLE Users (
    UserId              INT IDENTITY(1,1) PRIMARY KEY,
    UserName            NVARCHAR(100)  NOT NULL UNIQUE,
    FullName            NVARCHAR(150)  NOT NULL,
    Email               NVARCHAR(150)  NULL,
    Phone               NVARCHAR(30)   NULL,
    Position            NVARCHAR(100)  NULL,              -- Team Leader, Kidemli Uzman
    DepartmentId        INT            NULL REFERENCES Departments(DepartmentId),
    FirmId              INT            NULL REFERENCES Firms(FirmId),   -- NULL = MIP personeli
    IsFirmAdmin         BIT            NOT NULL DEFAULT 0, -- firma kendi alt kullanicisini acar
    ExternalId          NVARCHAR(100)  NULL,               -- AD objectId
    PasswordHash        NVARCHAR(255)  NULL,               -- sadece firma kullanicilari icin
    IsActive            BIT            NOT NULL DEFAULT 1,
    LastLoginAt         DATETIME2      NULL,
    CreatedAt           DATETIME2      NOT NULL DEFAULT SYSUTCDATETIME()
);
CREATE INDEX IX_Users_Firm ON Users(FirmId) WHERE FirmId IS NOT NULL;

CREATE TABLE Roles (
    RoleId              INT IDENTITY(1,1) PRIMARY KEY,
    Code                NVARCHAR(40)   NOT NULL UNIQUE,   -- REQUESTER, SUPERVISOR, DEPT_HEAD,
    Name                NVARCHAR(100)  NOT NULL,          -- BUDGET, ACCOUNTING, FIRM_USER, ADMIN
    Scope               NVARCHAR(20)   NOT NULL           -- INTERNAL | EXTERNAL
);

CREATE TABLE UserRoles (
    UserId              INT NOT NULL REFERENCES Users(UserId),
    RoleId              INT NOT NULL REFERENCES Roles(RoleId),
    DepartmentId        INT NULL REFERENCES Departments(DepartmentId), -- rol departman kapsamliysa
    PRIMARY KEY (UserId, RoleId)
);

-- Lokasyon agaci: "D Kapi > EMH-2 > Atolye Binasi"
CREATE TABLE Locations (
    LocationId          INT IDENTITY(1,1) PRIMARY KEY,
    Code                NVARCHAR(30)   NULL,
    Name                NVARCHAR(150)  NOT NULL,
    ParentLocationId    INT            NULL REFERENCES Locations(LocationId),
    FullPath            NVARCHAR(500)  NULL,              -- denormalize, arama icin
    IsActive            BIT            NOT NULL DEFAULT 1
);

/* -------------------------------------------------------------------------
   2. HIZMET KATALOGU
   Vinc de fiber de burada. Fark sadece Unit degeri.
   ------------------------------------------------------------------------- */

CREATE TABLE ServiceCategories (
    ServiceId           INT IDENTITY(1,1) PRIMARY KEY,
    Code                NVARCHAR(40)   NOT NULL UNIQUE,   -- CHERRY_PICKER, FIBER_CABLING
    Name                NVARCHAR(150)  NOT NULL,
    Unit                NVARCHAR(20)   NOT NULL,          -- HOUR | DAY | SHIFT | METER | PIECE
    RequiresTimeTracking BIT           NOT NULL DEFAULT 0,-- 1 ise baslangic/bitis saati zorunlu
    RequiresVehicle     BIT            NOT NULL DEFAULT 0,-- 1 ise plaka/operator alanlari acilir
    IsActive            BIT            NOT NULL DEFAULT 1,
    CONSTRAINT CK_Service_Unit CHECK (Unit IN ('HOUR','DAY','SHIFT','METER','PIECE'))
);

-- Kapasite / tip kirilimi. Fiyati bu belirliyor: "60 Ton Sepetli" != "30 Ton Sepetli"
CREATE TABLE ServiceVariants (
    VariantId           INT IDENTITY(1,1) PRIMARY KEY,
    ServiceId           INT            NOT NULL REFERENCES ServiceCategories(ServiceId),
    Code                NVARCHAR(40)   NOT NULL,
    Name                NVARCHAR(150)  NOT NULL,          -- 60 Ton Sepetli
    Capacity            NVARCHAR(50)   NULL,
    IsActive            BIT            NOT NULL DEFAULT 1,
    CONSTRAINT UQ_Variant UNIQUE (ServiceId, Code)
);

-- Araclar (opsiyonel). Plaka serbest metin de girilebilir; bu tablo tanimliysa secim listesi olur.
CREATE TABLE Equipment (
    EquipmentId         INT IDENTITY(1,1) PRIMARY KEY,
    FirmId              INT            NOT NULL REFERENCES Firms(FirmId),
    VariantId           INT            NULL REFERENCES ServiceVariants(VariantId),
    LicensePlate        NVARCHAR(20)   NULL,              -- 33 DC 151
    Description         NVARCHAR(200)  NULL,
    IsActive            BIT            NOT NULL DEFAULT 1
);

/* -------------------------------------------------------------------------
   3. SOZLESME VE FIYATLANDIRMA
   Fiyat ve kural burada. Kod tarafinda formul YOK, sadece parametre uygulanir.
   Degisiklik = yeni satir (ValidFrom/ValidTo). UPDATE ile fiyat degistirilmez.
   ------------------------------------------------------------------------- */

CREATE TABLE Contracts (
    ContractId          INT IDENTITY(1,1) PRIMARY KEY,
    FirmId              INT            NOT NULL REFERENCES Firms(FirmId),
    ContractNo          NVARCHAR(50)   NOT NULL,
    Title               NVARCHAR(200)  NULL,
    StartDate           DATE           NOT NULL,
    EndDate             DATE           NOT NULL,
    Currency            NVARCHAR(3)    NOT NULL DEFAULT 'TRY',
    Status              NVARCHAR(20)   NOT NULL DEFAULT 'ACTIVE', -- DRAFT|ACTIVE|EXPIRED|TERMINATED
    Notes               NVARCHAR(1000) NULL,
    CreatedAt           DATETIME2      NOT NULL DEFAULT SYSUTCDATETIME(),
    CreatedBy           INT            NULL REFERENCES Users(UserId),
    CONSTRAINT UQ_Contract UNIQUE (FirmId, ContractNo)
);

CREATE TABLE ContractLines (
    ContractLineId      INT IDENTITY(1,1) PRIMARY KEY,
    ContractId          INT            NOT NULL REFERENCES Contracts(ContractId),
    ServiceId           INT            NOT NULL REFERENCES ServiceCategories(ServiceId),
    VariantId           INT            NULL REFERENCES ServiceVariants(VariantId),

    UnitPrice           DECIMAL(18,4)  NOT NULL,
    Currency            NVARCHAR(3)    NOT NULL DEFAULT 'TRY',

    /* --- Fiyatlandirma parametreleri (MIP arayuzden doldurur) --- */
    MinBillableQuantity DECIMAL(18,4)  NULL,   -- min 4 saat gibi
    RoundingRule        NVARCHAR(20)   NOT NULL DEFAULT 'NONE',
                        -- NONE | UP_15 | UP_30 | UP_60 | NEAREST_15 | NEAREST_30 | NEAREST_60
    DayThresholdHours   DECIMAL(9,2)   NULL,   -- 8 saati asarsa 1 gun olarak faturalanir
    DailyPrice          DECIMAL(18,4)  NULL,   -- gun esigi asildiginda uygulanacak fiyat
    MobilizationFee     DECIMAL(18,4)  NULL,   -- sabit gidis/nakliye bedeli (sefer basina;
                        -- satira DEGIL, WorkRecords seviyesinde bir kez uygulanir)
    MaxQuantityPerRecord DECIMAL(18,4) NULL,   -- tek kayitta ust sinir (anomali kontrolu)

    ValidFrom           DATE           NOT NULL,
    ValidTo             DATE           NULL,   -- NULL = suresiz
    IsActive            BIT            NOT NULL DEFAULT 1,
    CreatedAt           DATETIME2      NOT NULL DEFAULT SYSUTCDATETIME(),
    CreatedBy           INT            NULL REFERENCES Users(UserId),

    CONSTRAINT CK_Rounding CHECK (RoundingRule IN
        ('NONE','UP_15','UP_30','UP_60','NEAREST_15','NEAREST_30','NEAREST_60'))
);
CREATE INDEX IX_ContractLines_Lookup
    ON ContractLines(ContractId, ServiceId, VariantId, ValidFrom, ValidTo);

-- Ek ucretler: mesai, gece, hafta sonu, tatil carpanlari.
-- Faz 1'de bos kalabilir; sema hazir olsun diye var.
CREATE TABLE ContractLineSurcharges (
    SurchargeId         INT IDENTITY(1,1) PRIMARY KEY,
    ContractLineId      INT            NOT NULL REFERENCES ContractLines(ContractLineId),
    SurchargeType       NVARCHAR(30)   NOT NULL,  -- OVERTIME|NIGHT|WEEKEND|HOLIDAY
    Multiplier          DECIMAL(9,4)   NULL,      -- 1.5 gibi
    FixedAmount         DECIMAL(18,4)  NULL,
    AppliesFromHour     TIME           NULL,      -- gece tarifesi baslangici
    AppliesToHour       TIME           NULL,
    IsActive            BIT            NOT NULL DEFAULT 1
);

/* -------------------------------------------------------------------------
   4. DONEM VE BELGE NUMARASI
   ------------------------------------------------------------------------- */

CREATE TABLE Periods (
    PeriodId            INT IDENTITY(1,1) PRIMARY KEY,
    Year                INT            NOT NULL,
    Month               INT            NOT NULL,
    Status              NVARCHAR(20)   NOT NULL DEFAULT 'OPEN', -- OPEN|CLOSED|REOPENED
    ClosedAt            DATETIME2      NULL,
    ClosedBy            INT            NULL REFERENCES Users(UserId),
    ReopenedAt          DATETIME2      NULL,
    ReopenedBy          INT            NULL REFERENCES Users(UserId),
    ReopenReason        NVARCHAR(500)  NULL,
    CONSTRAINT UQ_Period UNIQUE (Year, Month),
    CONSTRAINT CK_Month CHECK (Month BETWEEN 1 AND 12)
);

CREATE TABLE DocumentSeries (
    SeriesId            INT IDENTITY(1,1) PRIMARY KEY,
    DocumentType        NVARCHAR(30)   NOT NULL,  -- REQUEST | WORK_RECORD
    Prefix              NVARCHAR(10)   NOT NULL,  -- CPR, WR
    Year                INT            NOT NULL,
    LastNumber          INT            NOT NULL DEFAULT 0,
    Padding             INT            NOT NULL DEFAULT 5,
    CONSTRAINT UQ_Series UNIQUE (DocumentType, Year)
);

/* -------------------------------------------------------------------------
   5. TALEP (IS ONCESI)
   ------------------------------------------------------------------------- */

CREATE TABLE Requests (
    RequestId           INT IDENTITY(1,1) PRIMARY KEY,
    DocumentNo          NVARCHAR(30)   NOT NULL UNIQUE,
    Status              NVARCHAR(30)   NOT NULL DEFAULT 'DRAFT',
                        -- DRAFT|SUBMITTED|PENDING|APPROVED|REJECTED|REVISION_REQUESTED|CANCELLED

    RequestedByUserId   INT            NOT NULL REFERENCES Users(UserId),
    DepartmentId        INT            NOT NULL REFERENCES Departments(DepartmentId),
    FirmId              INT            NULL REFERENCES Firms(FirmId), -- atanan firma

    IssueDate           DATE           NOT NULL,
    RequestedDate       DATE           NOT NULL,
    RequestedStartTime  TIME           NULL,
    RequestedEndTime    TIME           NULL,
    LocationId          INT            NULL REFERENCES Locations(LocationId),
    LocationText        NVARCHAR(300)  NULL,   -- agacta yoksa serbest metin
    WorkDescription     NVARCHAR(1000) NULL,

    Notes               NVARCHAR(1000) NULL,
    CreatedAt           DATETIME2      NOT NULL DEFAULT SYSUTCDATETIME(),
    UpdatedAt           DATETIME2      NULL
);
CREATE INDEX IX_Requests_Status ON Requests(Status, RequestedDate);

CREATE TABLE RequestLines (
    RequestLineId       INT IDENTITY(1,1) PRIMARY KEY,
    RequestId           INT            NOT NULL REFERENCES Requests(RequestId),
    ServiceId           INT            NOT NULL REFERENCES ServiceCategories(ServiceId),
    VariantId           INT            NULL REFERENCES ServiceVariants(VariantId),
    EstimatedQuantity   DECIMAL(18,4)  NULL,
    LineNo              INT            NOT NULL DEFAULT 1
);

/* -------------------------------------------------------------------------
   6. CALISMA KAYDI (IS SONRASI) - Sistemin kalbi
   Kagit formun dijital ikizi. Sari fisin de ikizi (ExternalReceiptNo).
   ------------------------------------------------------------------------- */

CREATE TABLE WorkRecords (
    WorkRecordId        INT IDENTITY(1,1) PRIMARY KEY,
    DocumentNo          NVARCHAR(30)   NOT NULL UNIQUE,
    Status              NVARCHAR(30)   NOT NULL DEFAULT 'DRAFT',
                        -- DRAFT|SUBMITTED|PENDING|APPROVED|REJECTED|REVISION_REQUESTED|CANCELLED|LOCKED
                        -- LOCKED: donem kapatilirken APPROVED kayitlara uygulanir.
                        -- Kullanici eylemiyle ulasilmaz; tek kaynagi PeriodLockService'tir.
                        -- Terminaldir: cikisin TEK yolu donemin yeniden acilmasidir.
    IntegrationStatus   NVARCHAR(20)   NOT NULL DEFAULT 'NOT_SENT',
                        -- NOT_SENT | SENT | FAILED | CONFIRMED

    RequestId           INT            NULL REFERENCES Requests(RequestId), -- opsiyonel bag
    FirmId              INT            NOT NULL REFERENCES Firms(FirmId),
    ContractId          INT            NOT NULL REFERENCES Contracts(ContractId),
    PeriodId            INT            NOT NULL REFERENCES Periods(PeriodId),

    WorkDate            DATE           NOT NULL,
    StartTime           TIME           NULL,
    EndTime             TIME           NULL,
    SpansMidnight       BIT            NOT NULL DEFAULT 0,  -- gece vardiyasi

    LocationId          INT            NULL REFERENCES Locations(LocationId),
    LocationText        NVARCHAR(300)  NULL,
    WorkDescription     NVARCHAR(1000) NULL,

    -- MIP tarafi (iki farkli kisi olabiliyor)
    RequestedByUserId   INT            NULL REFERENCES Users(UserId), -- formu talep eden
    WitnessedByUserId   INT            NULL REFERENCES Users(UserId), -- sahada isi yaptiran yetkili
    DepartmentId        INT            NULL REFERENCES Departments(DepartmentId),

    -- Firma tarafi
    OperatorName        NVARCHAR(150)  NULL,
    EquipmentId         INT            NULL REFERENCES Equipment(EquipmentId),
    LicensePlate        NVARCHAR(20)   NULL,   -- Equipment yoksa serbest
    PersonnelCount      INT            NULL,

    -- Dis fisin dijital ikizi (foto YOK, sadece referans)
    ExternalReceiptNo   NVARCHAR(40)   NULL,   -- 0078
    ExternalReceiptDate DATE           NULL,

    -- Sefer basi nakliye/mobilizasyon bedeli. SATIR degil KAYIT seviyesindedir:
    -- kayitta kac hizmet satiri olursa olsun yalnizca BIR kez uygulanir.
    MobilizationFee     DECIMAL(18,4)  NULL,

    -- Hesaplanan toplam (denormalize) = SUM(WorkRecordLines.LineAmount) + ISNULL(MobilizationFee, 0)
    TotalAmount         DECIMAL(18,4)  NULL,
    Currency            NVARCHAR(3)    NULL,

    EnteredByUserId     INT            NOT NULL REFERENCES Users(UserId), -- kim girdi
    SubmittedAt         DATETIME2      NULL,
    ApprovedAt          DATETIME2      NULL,
    CreatedAt           DATETIME2      NOT NULL DEFAULT SYSUTCDATETIME(),
    UpdatedAt           DATETIME2      NULL,

    -- Duzeltme zinciri: silme yok, yeni versiyon
    RevisionOfId        INT            NULL REFERENCES WorkRecords(WorkRecordId),
    RevisionReason      NVARCHAR(500)  NULL,
    IsSuperseded        BIT            NOT NULL DEFAULT 0
);
CREATE INDEX IX_WorkRecords_Period  ON WorkRecords(PeriodId, FirmId, Status);
CREATE INDEX IX_WorkRecords_Date    ON WorkRecords(WorkDate, FirmId);
-- Mukerrer kayit UYARISI icin (bloklamaz, uygulama katmani uyarir)
CREATE INDEX IX_WorkRecords_DupCheck ON WorkRecords(FirmId, WorkDate, LicensePlate, StartTime);

CREATE TABLE WorkRecordLines (
    WorkRecordLineId    INT IDENTITY(1,1) PRIMARY KEY,
    WorkRecordId        INT            NOT NULL REFERENCES WorkRecords(WorkRecordId),
    LineNo              INT            NOT NULL DEFAULT 1,

    ServiceId           INT            NOT NULL REFERENCES ServiceCategories(ServiceId),
    VariantId           INT            NULL REFERENCES ServiceVariants(VariantId),

    RawQuantity         DECIMAL(18,4)  NOT NULL,  -- ham miktar (7.5 saat / 162 metre)
    BillableQuantity    DECIMAL(18,4)  NOT NULL,  -- kurallar uygulandiktan sonra (8 saat)
    Unit                NVARCHAR(20)   NOT NULL,

    -- SNAPSHOT: sozlesme sonradan degisse bile bu kayit bozulmaz
    ContractLineId      INT            NULL REFERENCES ContractLines(ContractLineId),
    UnitPriceSnapshot   DECIMAL(18,4)  NOT NULL,
    PricingRuleSnapshot NVARCHAR(MAX)  NULL,      -- JSON: uygulanan kurallar
    SurchargeAmount     DECIMAL(18,4)  NOT NULL DEFAULT 0,
    LineAmount          DECIMAL(18,4)  NOT NULL,  -- = BaseAmount + SurchargeAmount.
                        -- Mobilizasyon bedeli HARIC (o, WorkRecords.MobilizationFee'de).
    Currency            NVARCHAR(3)    NOT NULL DEFAULT 'TRY',

    Description         NVARCHAR(500)  NULL,
    IsManualOverride    BIT            NOT NULL DEFAULT 0,  -- motor disi mudahale
    OverrideReason      NVARCHAR(500)  NULL,

    /* --- Satir bazli itiraz ---
       Onaylayan, kaydin TAMAMINI reddetmek yerine tek bir satirina itiraz eder.
       Itiraz edilen satir varsa kayit REVISION_REQUESTED olur; 40 satirlik bir
       kayitta 1 satir yuzunden tum ay beklemez. */
    IsObjected          BIT            NOT NULL DEFAULT 0,
    ObjectionReason     NVARCHAR(500)  NULL,
    ObjectedByUserId    INT            NULL REFERENCES Users(UserId),
    ObjectedAt          DATETIME2      NULL
);
CREATE INDEX IX_WorkRecordLines_Record ON WorkRecordLines(WorkRecordId);
CREATE INDEX IX_WorkRecordLines_Objected ON WorkRecordLines(WorkRecordId, IsObjected)
    WHERE IsObjected = 1;

/* -------------------------------------------------------------------------
   7. ONAY AKISI
   Zincir kodda degil burada. Adim sahipleri degisirse sema degismez.
   ------------------------------------------------------------------------- */

CREATE TABLE ApprovalFlows (
    FlowId              INT IDENTITY(1,1) PRIMARY KEY,
    Code                NVARCHAR(40)   NOT NULL UNIQUE,
    Name                NVARCHAR(150)  NOT NULL,
    DocumentType        NVARCHAR(30)   NOT NULL,  -- REQUEST | WORK_RECORD
    ServiceId           INT            NULL REFERENCES ServiceCategories(ServiceId), -- NULL = varsayilan
    IsActive            BIT            NOT NULL DEFAULT 1
);

CREATE TABLE ApprovalFlowSteps (
    FlowStepId          INT IDENTITY(1,1) PRIMARY KEY,
    FlowId              INT            NOT NULL REFERENCES ApprovalFlows(FlowId),
    StepNo              INT            NOT NULL,
    RoleId              INT            NOT NULL REFERENCES Roles(RoleId),
    Name                NVARCHAR(100)  NOT NULL,   -- "Ekipman Supervisor Onayi"
    IsMandatory         BIT            NOT NULL DEFAULT 1,
    AmountThreshold     DECIMAL(18,4)  NULL,       -- bu tutarin ustunde devreye girer
    ReminderAfterHours  INT            NULL,       -- hatirlatma e-postasi
    EscalateAfterHours  INT            NULL,       -- ustune eskalasyon
    CONSTRAINT UQ_FlowStep UNIQUE (FlowId, StepNo)
);

CREATE TABLE Approvals (
    ApprovalId          INT IDENTITY(1,1) PRIMARY KEY,
    DocumentType        NVARCHAR(30)   NOT NULL,   -- REQUEST | WORK_RECORD
    DocumentId          INT            NOT NULL,
    FlowStepId          INT            NULL REFERENCES ApprovalFlowSteps(FlowStepId),
    StepNo              INT            NOT NULL,

    AssignedToUserId    INT            NULL REFERENCES Users(UserId),
    AssignedToRoleId    INT            NULL REFERENCES Roles(RoleId),
    DecidedByUserId     INT            NULL REFERENCES Users(UserId),

    Decision            NVARCHAR(20)   NULL,       -- APPROVED|REJECTED|REVISION_REQUESTED
    Comment             NVARCHAR(1000) NULL,
    AssignedAt          DATETIME2      NOT NULL DEFAULT SYSUTCDATETIME(),
    DecidedAt           DATETIME2      NULL,
    ReminderSentAt      DATETIME2      NULL
);
CREATE INDEX IX_Approvals_Doc     ON Approvals(DocumentType, DocumentId, StepNo);
CREATE INDEX IX_Approvals_Pending ON Approvals(AssignedToUserId, Decision) WHERE Decision IS NULL;

/* -------------------------------------------------------------------------
   8. CIKTI, IZ VE BILDIRIM
   ------------------------------------------------------------------------- */

-- Uretilen PDF. Sablon degisse bile eski belge degismis gorunmesin diye hash tutulur.
-- Belge YENIDEN uretilirse eski satir SILINMEZ, yeni bir surum eklenir.
-- Eldeki kagidin dogrulama kodu, yenisi uretildikten sonra da calisir.
CREATE TABLE GeneratedDocuments (
    GeneratedDocumentId INT IDENTITY(1,1) PRIMARY KEY,
    DocumentType        NVARCHAR(30)   NOT NULL,  -- REQUEST | WORK_RECORD | PERIOD
    DocumentId          INT            NOT NULL,  -- PERIOD ise PeriodId
    Kind                NVARCHAR(30)   NOT NULL,  -- FORM_PDF | MONTHLY_SUMMARY_PDF | EXPORT_XLSX
    FirmId              INT            NULL REFERENCES Firms(FirmId),
                                                  -- Aylik icmalde DocumentId donemi gosterir;
                                                  -- hangi firmanin icmali oldugu ancak burada belli olur.
    FileName            NVARCHAR(255)  NOT NULL,
    StoragePath         NVARCHAR(500)  NOT NULL,  -- koke gore yol: {yyyy}/{MM}/{zaman}-{ad}
    ContentHash         NVARCHAR(64)   NOT NULL,  -- SHA-256
    VerificationCode    NVARCHAR(40)   NULL,      -- PDF uzerindeki QR bu koda gider (Guid tabanli)
    TemplateVersion     NVARCHAR(20)   NULL,
    TotalAmount         DECIMAL(18,4)  NULL,      -- Belgenin UZERINDE YAZAN tutar. Kayit sonradan
    Currency            NVARCHAR(3)    NULL,      -- revize olsa bile dogrulama sayfasi kagittakini gosterir.
    GeneratedAt         DATETIME2      NOT NULL DEFAULT SYSUTCDATETIME(),
    GeneratedBy         INT            NULL REFERENCES Users(UserId)
);

-- Dogrulama kodu acik bir sayfadan sorgulanir; iki belgeye ayni kod dusemez.
CREATE UNIQUE INDEX UX_GeneratedDocuments_VerificationCode
    ON GeneratedDocuments(VerificationCode) WHERE VerificationCode IS NOT NULL;

-- Bir belgenin tum surumlerini uretim sirasina gore cekmek icin.
CREATE INDEX IX_GeneratedDocuments_Document
    ON GeneratedDocuments(DocumentType, DocumentId, Kind, GeneratedAt);

-- Nadiren kullanilacak. Fis fotosu icin DEGIL; tutanak/yazisma icin.
CREATE TABLE Attachments (
    AttachmentId        INT IDENTITY(1,1) PRIMARY KEY,
    DocumentType        NVARCHAR(30)   NOT NULL,
    DocumentId          INT            NOT NULL,
    FileName            NVARCHAR(255)  NOT NULL,
    StoragePath         NVARCHAR(500)  NOT NULL,
    ContentType         NVARCHAR(100)  NULL,
    FileSizeBytes       BIGINT         NULL,
    UploadedBy          INT            NULL REFERENCES Users(UserId),
    UploadedAt          DATETIME2      NOT NULL DEFAULT SYSUTCDATETIME()
);

-- Islak imzanin yerini alan tek sey. Her degisiklik buraya duser.
CREATE TABLE AuditLog (
    AuditId             BIGINT IDENTITY(1,1) PRIMARY KEY,
    TableName           NVARCHAR(100)  NOT NULL,
    RecordId            INT            NOT NULL,
    Action              NVARCHAR(20)   NOT NULL,  -- INSERT|UPDATE|STATUS_CHANGE|APPROVE|REJECT
    FieldName           NVARCHAR(100)  NULL,
    OldValue            NVARCHAR(MAX)  NULL,
    NewValue            NVARCHAR(MAX)  NULL,
    Reason              NVARCHAR(500)  NULL,
    UserId              INT            NULL REFERENCES Users(UserId),
    IpAddress           NVARCHAR(45)   NULL,
    OccurredAt          DATETIME2      NOT NULL DEFAULT SYSUTCDATETIME()
);
CREATE INDEX IX_Audit_Record ON AuditLog(TableName, RecordId, OccurredAt);

-- Semadaki "Reminder Email" kutulari
CREATE TABLE Notifications (
    NotificationId      BIGINT IDENTITY(1,1) PRIMARY KEY,
    UserId              INT            NULL REFERENCES Users(UserId),
    Email               NVARCHAR(150)  NULL,
    Channel             NVARCHAR(20)   NOT NULL DEFAULT 'EMAIL', -- EMAIL|INAPP
    TemplateCode        NVARCHAR(50)   NOT NULL,
    Subject             NVARCHAR(300)  NULL,
    Body                NVARCHAR(MAX)  NULL,
    DocumentType        NVARCHAR(30)   NULL,
    DocumentId          INT            NULL,
    Status              NVARCHAR(20)   NOT NULL DEFAULT 'QUEUED', -- QUEUED|SENT|FAILED
    RetryCount          INT            NOT NULL DEFAULT 0,
    CreatedAt           DATETIME2      NOT NULL DEFAULT SYSUTCDATETIME(),
    SentAt              DATETIME2      NULL
);
CREATE INDEX IX_Notifications_Queue ON Notifications(Status, CreatedAt);

/* -------------------------------------------------------------------------
   9. FAZ 2 - ORACLE ENTEGRASYONU (sema hazir, kullanim sonra)
   ------------------------------------------------------------------------- */

CREATE TABLE IntegrationQueue (
    QueueId             BIGINT IDENTITY(1,1) PRIMARY KEY,
    TargetSystem        NVARCHAR(30)   NOT NULL DEFAULT 'ORACLE',
    DocumentType        NVARCHAR(30)   NOT NULL,
    DocumentId          INT            NOT NULL,
    Payload             NVARCHAR(MAX)  NULL,
    Status              NVARCHAR(20)   NOT NULL DEFAULT 'PENDING', -- PENDING|SENT|FAILED|CONFIRMED
    ExternalRef         NVARCHAR(100)  NULL,
    ErrorMessage        NVARCHAR(MAX)  NULL,
    RetryCount          INT            NOT NULL DEFAULT 0,
    CreatedAt           DATETIME2      NOT NULL DEFAULT SYSUTCDATETIME(),
    ProcessedAt         DATETIME2      NULL
);
