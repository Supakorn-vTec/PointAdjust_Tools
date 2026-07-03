vtecPoint - คู่มือใช้งาน (Support Team)
========================================

1. แตกไฟล์ zip ไปยังโฟลเดอร์ที่ต้องการ

2. แก้ไขการเชื่อมต่อ Database ในไฟล์ appsettings.json
   - เปิดด้วย Notepad
   - แก้ค่า ConnectionString ให้ตรงกับ Server ของลูกค้า

3. ดับเบิลคลิก Start.bat เพื่อเปิดโปรแกรม
   - Browser จะเปิดที่ http://localhost:5252 อัตโนมัติ
   - ต้อง Login ด้วย Staff Login / Password จากตาราง staffs ก่อนใช้งาน

4. ขั้นตอนใช้งาน
   - กด "เปิดไฟล์ Excel" (คอลัมน์: MemberCode, Point, Note)
   - ตรวจสอบข้อมูลในตาราง
   - กด "ยืนยันปรับแต้ม" เมื่อทุกแถวผ่าน

หมายเหตุ:
- ต้องเชื่อมต่อ Network ไปยัง SQL Server ได้
- ไม่ต้องติดตั้ง .NET (รวม runtime ไว้ใน package แล้ว)
- ปิดโปรแกรม: กด Ctrl+C ในหน้าต่าง Command หรือปิดหน้าต่าง
