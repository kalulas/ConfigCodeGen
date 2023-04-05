﻿using System;
using System.Collections.Generic;
using System.IO;
using LitJson;
using System.Text;

namespace ConfigCodeGenLib.ConfigReader
{
    /// <summary>
    /// Information about a single config file, for example: SomeTable.csv
    /// </summary>
    public abstract class ConfigInfo
    {
        #region Fields

        private const string ATTRIBUTES_KEY = "Attributes";
        private const string CONFIG_NAME_KEY = "ConfigName";
        private const string USAGE_KEY = "Usage";
        
        protected readonly string m_ConfigFilePath;
        protected readonly string m_RelatedJsonFilePath;

        #endregion

        #region Properties
        
        public bool HasJsonConfig { get; private set; }

        public string ConfigName { get; }
        public EConfigType ConfigType { get; }

        /// <summary>
        /// AttributeName -> AttributeInfo instance
        /// </summary>
        protected readonly Dictionary<string, ConfigAttributeInfo> ConfigAttributeDict;

        /// <summary>
        /// UsageName -> UsageInfo
        /// </summary>
        protected readonly Dictionary<string, ConfigUsageInfo> ConfigUsageDict;

        public ICollection<ConfigAttributeInfo> AttributeInfos => ConfigAttributeDict.Values;

        #endregion

        public ConfigInfo(EConfigType configType, string configName, string configFilePath, string relatedJsonFilePath)
        {
            ConfigType = configType;
            ConfigName = configName;
            ConfigAttributeDict = new Dictionary<string, ConfigAttributeInfo>();
            ConfigUsageDict = new Dictionary<string, ConfigUsageInfo>();
            m_ConfigFilePath = configFilePath;
            m_RelatedJsonFilePath = relatedJsonFilePath;
            PrepareUsageDict();
        }

        #region Private Methods

        private void PrepareUsageDict()
        {
            foreach (var usage in Configuration.ConfigUsageType)
            {
                ConfigUsageDict[usage] = new ConfigUsageInfo
                {
                    // default value
                    ExportName = ConfigName
                };
            }
        }

        #endregion

        #region LOAD ATTRIBUTES FROM CONFIG & JSON

        /// <summary>
        /// NOTICE: clean up attributes first
        /// </summary>
        public abstract ConfigInfo ReadConfigFileAttributes();

        /// <summary>
        /// add json related information to attributes
        /// </summary>
        public ConfigInfo ReadJsonFileAttributes()
        {
            HasJsonConfig = !string.IsNullOrEmpty(m_RelatedJsonFilePath) && File.Exists(m_RelatedJsonFilePath);
            if (!HasJsonConfig)
            {
                return this;
            }
            
            var encoding = new UTF8Encoding(Configuration.UseUTF8WithBOM);
            var jsonContent = File.ReadAllText(m_RelatedJsonFilePath, encoding);
            var jsonData = JsonMapper.ToObject(jsonContent);
            if (!jsonData.ContainsKey(CONFIG_NAME_KEY))
            {
                Debugger.LogError("'{0}' not found in json file {1}", CONFIG_NAME_KEY, m_RelatedJsonFilePath);
                return this;
            }
            
            var configName = jsonData[CONFIG_NAME_KEY].ToString();
            if (configName != ConfigName)
            {
                Debugger.LogWarning($"ConfigName doesn't match: json({configName}), runtime({ConfigName})");
            }

            if (!jsonData.ContainsKey(USAGE_KEY))
            {
                Debugger.LogError("'{0}' not found in json file {1}", USAGE_KEY, m_RelatedJsonFilePath);
                return this;
            }
            
            var usageJsonData = jsonData[USAGE_KEY];
            foreach (var usageConfig in usageJsonData)
            {
                var (usage, usageInfoJsonData) = (KeyValuePair<string,JsonData>)usageConfig;
                var usageInfo = JsonMapper.ToObject<ConfigUsageInfo>(usageInfoJsonData.ToJson());
                if (ConfigUsageDict.ContainsKey(usage))
                {
                    ConfigUsageDict[usage] = usageInfo;
                }
                else
                {
                    Debugger.LogWarning($"Usage '{usage}' is not available under current configuration, ignore");
                }
            }
            
            if (!jsonData.ContainsKey(ATTRIBUTES_KEY))
            {
                Debugger.LogError("'{0}' not found in json file {1}", ATTRIBUTES_KEY, m_RelatedJsonFilePath);
                return this;
            }

            var attributesJsonData = jsonData[ATTRIBUTES_KEY];
            foreach (var attributeJson in attributesJsonData)
            {
                var attributeJsonData = attributeJson as JsonData;
                if (attributeJsonData == null)
                {
                    continue;
                }

                var name = attributeJsonData[ConfigAttributeInfo.ATTRIBUTE_NAME_KEY].ToString();
                if (!ConfigAttributeDict.ContainsKey(name))
                {
                    Debugger.LogError("attribute '{0}' not found in config file {1}", name, m_ConfigFilePath);
                    continue;
                }
                
                try
                {
                    ConfigAttributeDict[name].SetJsonFileInfo(attributeJsonData);
                }
                catch (Exception e)
                {
                    Debugger.LogError($"ReadJsonFileAttributes during attribute '{name}' of file '{ConfigName}' failed: {e.Message}");
                    throw;
                }
            }

            return this;
        }

        #endregion

        #region Seriailize

        internal void SaveJsonFile(string jsonFilePath)
        {
            var builder = new StringBuilder();
            var writer = new JsonWriter(builder)
            {
                PrettyPrint = true
            };

            writer.WriteObjectStart();
            writer.WritePropertyName(CONFIG_NAME_KEY);
            writer.Write(ConfigName);
            
            writer.WritePropertyName(USAGE_KEY);
            JsonMapper.ToJson(ConfigUsageDict, writer);

            writer.WritePropertyName(ATTRIBUTES_KEY);
            writer.WriteArrayStart();
            foreach (var attribute in ConfigAttributeDict)
            {
                attribute.Value.WriteToJson(writer);
            }

            writer.WriteArrayEnd();
            writer.WriteObjectEnd();

            // make sure the directory existed
            Directory.CreateDirectory(Path.GetDirectoryName(jsonFilePath));
            var encoding = new UTF8Encoding(Configuration.UseUTF8WithBOM);
            using (var fs = File.Open(jsonFilePath, FileMode.Create))
            {
                using (var sw = new StreamWriter(fs, encoding))
                {
                    sw.Write(builder.ToString());
                }
            }

            HasJsonConfig = true;
        }

        #endregion

        #region Public API

        public bool TryGetUsageInfo(string usage, out ConfigUsageInfo usageInfo)
        {
            return ConfigUsageDict.TryGetValue(usage, out usageInfo);
        }

        /// <summary>
        /// Return ExportName if <paramref name="usage"/> was found, else return original <see cref="ConfigName"/>
        /// </summary>
        /// <param name="usage"></param>
        /// <returns></returns>
        public string GetExportName(string usage)
        {
            if (ConfigUsageDict.TryGetValue(usage, out var usageInfo))
            {
                return usageInfo.ExportName;
            }

            return ConfigName;
        }

        #endregion
    }
}