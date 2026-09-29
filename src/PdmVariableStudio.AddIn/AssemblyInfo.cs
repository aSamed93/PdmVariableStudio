using System.Reflection;
using System.Runtime.InteropServices;

[assembly: AssemblyTitle("PDM Variable Studio Add-in")]
[assembly: AssemblyDescription("PDM Variable Studio uygulamasını başlatan ince eklenti")]
[assembly: AssemblyCompany("Abdussamed Tarlak")]

// Yalnızca VariableStudioAddIn sınıfı COM'a açılır; onda ayrıca [ComVisible(true)] var.
// Assembly düzeyinde false demek, ileride eklenen public bir tipin farkında olmadan COM'a
// sızmasını engelliyor. Kardeş eklentilerin (PDMetry, ErpSecim, BomAktarim) hepsi böyle.
[assembly: ComVisible(false)]

// Sürüm AÇIKÇA veriliyor. Aksi hâlde assembly 0.0.0.0 kimliğiyle derleniyor ve eski
// sürümle yenisi CLR açısından AYNI kimlikte oluyor. PDM Administration eklentiyi
// güncellerken eskisini süreçten kaldıramadığı için (.NET assembly'leri unload edilemez)
// bu belirsizlik kurulumu kilitleyebiliyor. Sürüm, GetAddInInfo'daki mlAddInVersion ile
// birlikte artırılır.
[assembly: AssemblyVersion("6.0.0.0")]
[assembly: AssemblyFileVersion("6.0.0.0")]
