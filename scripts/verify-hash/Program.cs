using System;
using BCrypt.Net;

var password = "SeedPassword1!";
var newHash = BCrypt.Net.BCrypt.HashPassword(password, workFactor: 10);
Console.WriteLine(newHash);
bool verify = BCrypt.Net.BCrypt.Verify(password, newHash);
Console.WriteLine($"Verify: {verify}");
