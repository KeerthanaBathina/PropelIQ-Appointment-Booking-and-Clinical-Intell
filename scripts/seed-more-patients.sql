INSERT INTO patients ("Id","TenantId","FullName","Email","PhoneNumber","DateOfBirth","PasswordHash","CreatedAt","UpdatedAt","SmsOptedOut","AutoSwapEnabled") VALUES
('b0000000-0000-0000-0000-000000000010','00000000-0000-0000-0000-000000000001','Liam Bennett','liam.bennett@upacip.dev','+15552010001','1983-03-15','',NOW(),NOW(),false,true),
('b0000000-0000-0000-0000-000000000011','00000000-0000-0000-0000-000000000001','Sophia Williams','sophia.williams@upacip.dev','+15552011001','1991-08-22','',NOW(),NOW(),false,true),
('b0000000-0000-0000-0000-000000000012','00000000-0000-0000-0000-000000000001','Noah Martinez','noah.martinez@upacip.dev','+15552012001','1975-11-07','',NOW(),NOW(),false,true),
('b0000000-0000-0000-0000-000000000013','00000000-0000-0000-0000-000000000001','Olivia Taylor','olivia.taylor@upacip.dev','+15552013001','2000-05-30','',NOW(),NOW(),false,true),
('b0000000-0000-0000-0000-000000000014','00000000-0000-0000-0000-000000000001','Ethan Brown','ethan.brown@upacip.dev','+15552014001','1968-02-14','',NOW(),NOW(),false,true),
('b0000000-0000-0000-0000-000000000015','00000000-0000-0000-0000-000000000001','Ava Wilson','ava.wilson@upacip.dev','+15552015001','1995-09-03','',NOW(),NOW(),false,true),
('b0000000-0000-0000-0000-000000000016','00000000-0000-0000-0000-000000000001','Mason Anderson','mason.anderson@upacip.dev','+15552016001','1959-12-19','',NOW(),NOW(),false,true),
('b0000000-0000-0000-0000-000000000017','00000000-0000-0000-0000-000000000001','Isabella Garcia','isabella.garcia@upacip.dev','+15552017001','1987-06-25','',NOW(),NOW(),false,true),
('b0000000-0000-0000-0000-000000000018','00000000-0000-0000-0000-000000000001','James Lee','james.lee@upacip.dev','+15552018001','1972-04-08','',NOW(),NOW(),false,true),
('b0000000-0000-0000-0000-000000000019','00000000-0000-0000-0000-000000000001','Charlotte White','charlotte.white@upacip.dev','+15552019001','1980-10-11','',NOW(),NOW(),false,true)
ON CONFLICT DO NOTHING;
