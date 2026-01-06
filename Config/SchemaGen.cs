
using Newtonsoft.Json.Schema.Generation;
using WorldTime.Config;

var gen = new JSchemaGenerator();
var sch = gen.Generate(typeof(Configuration));

File.WriteAllText("config.schema.json", sch.ToString());