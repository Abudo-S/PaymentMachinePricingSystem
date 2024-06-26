﻿using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LibDTO
{
    [BsonIgnoreExtraElements]
    public class DayRate
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; }

        [BsonElement]
        public int FreeMinutes { get; set; }

        [BsonElement]
        public double MaxAmount { get; set; }

        [BsonElement]
        public DayOfWeek CorrespondentDay { get; set; }
    }
}
