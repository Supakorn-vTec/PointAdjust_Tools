vtecPoint - คู่มือใช้งาน (Support Team)
========================================

1. แตกไฟล์ zip ไปยังโฟลเดอร์ที่ต้องการ

2. แก้ไขการเชื่อมต่อ Database ในไฟล์ appsettings.json
   - เปิดด้วย Notepad
   - แก้ค่า ConnectionString ให้ตรงกับ Server ของลูกค้า

3. เปิดโปรแกรม
   - ดับเบิลคลิก vtecPoint.exe (หน้าต่างโปรแกรม — ไม่ต้องเปิด browser)
   - หรือดับเบิลคลิก Start.bat

4. Login ด้วย Staff Login / Password จากตาราง staffs

5. ขั้นตอนใช้งาน
   - กด "เปิดไฟล์ Excel" (คอลัมน์: MemberCode, Point, Note)
   - ตรวจสอบข้อมูลในตาราง
   - กด "ยืนยันปรับแต้ม" เมื่อทุกแถวผ่าน

ไฟล์ใน package:
   - vtecPoint.exe         = หน้าต่างโปรแกรม (เปิดตัวนี้)
   - vtecPoint.Server.exe  = server ภายใน (เปิดอัตโนมัติ)

หมายเหตุ:
- ต้องเชื่อมต่อ Network ไปยัง SQL Server ได้
- ต้องติดตั้ง WebView2 Runtime (Windows 10/11 ส่วนใหญ่มีอยู่แล้ว)
- ไม่ต้องติดตั้ง .NET (รวม runtime ไว้ใน package แล้ว)
- ปิดโปรแกรม: ปิดหน้าต่าง vtecPoint
