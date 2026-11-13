using ScaleLab.HubServer;

var app = HubServerApp.Build(args);
await app.RunAsync();
