namespace MipRental.Domain.Enums;

public enum DocumentType
{
    REQUEST,
    WORK_RECORD,

    // Aylık icmal belgesi tek bir çalışma kaydına değil, bir DÖNEME aittir.
    // Hangi firmanın icmali olduğu GeneratedDocuments.FirmId'de tutulur.
    PERIOD
}
