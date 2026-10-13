using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using PokeChess.Content;
using PokeChess.Core.Pokemon;

namespace PokeChess.Core.Tests
{
    public sealed class RosterContentTests
    {
        static void Same(object expected,object actual,string path)
        {
            if(expected==null||actual==null){Assert.That(actual,Is.EqualTo(expected),path);return;}
            var type=expected.GetType();
            if(type.IsPrimitive||type.IsEnum||expected is string){Assert.That(actual,Is.EqualTo(expected),path);return;}
            if(expected is IEnumerable sequence)
            {
                var a=sequence.Cast<object>().ToArray();var b=((IEnumerable)actual).Cast<object>().ToArray();
                Assert.That(b.Length,Is.EqualTo(a.Length),path);
                for(int i=0;i<a.Length;i++)Same(a[i],b[i],path+"["+i+"]");return;
            }
            foreach(var p in type.GetProperties().Where(p=>p.GetIndexParameters().Length==0))
                Same(p.GetValue(expected),p.GetValue(actual),path+"."+p.Name);
        }
        [Test]
        public void MigratedContentPreservesEveryCombatField()
        {
            var roster=Resources.Load<RosterData>("GameRoster");roster.Validate();
            var definitions=PrototypeEvolutionRoster.WithEvolutions(PrototypeRoster.CreateDefinitions()).ToArray();
            Assert.That(roster.Pokemon.Length,Is.EqualTo(definitions.Length));
            var catalog=roster.CreateCatalog();foreach(var d in definitions)Same(d,catalog.Get(d.Id),d.Id);
            Same(PrototypeRoster.CreateDefinitions().Select(d=>d.Id).ToArray(),roster.ShopPokemon.Select(p=>p.Id).ToArray(),"shop");
            var skills=PrototypeRoster.CreateSkills();var statuses=PrototypeRoster.CreateStatuses();
            foreach(var s in roster.Skills){Assert.That(skills.TryGet(s.Id,out var old),Is.True);Same(old,s.ToCore(),s.Id);}
            foreach(var s in roster.Statuses)Same(statuses.Get(s.Id),s.ToCore(),s.Id);
        }
        [Test]
        public void DuplicateAndUnregisteredEntriesAreRejected()
        {
            var source=Resources.Load<RosterData>("GameRoster");var clone=UnityEngine.Object.Instantiate(source);
            try
            {
                clone.Pokemon=source.Pokemon.Concat(new[]{source.Pokemon[0]}).ToArray();
                Assert.Throws<InvalidOperationException>(()=>clone.Validate());
                clone.Pokemon=source.Pokemon;clone.Skills=source.Skills.Skip(1).ToArray();
                Assert.Throws<InvalidOperationException>(()=>clone.Validate());
                clone.Skills=source.Skills;clone.Statuses=Array.Empty<StatusData>();
                Assert.Throws<InvalidOperationException>(()=>clone.Validate());
            }
            finally{UnityEngine.Object.DestroyImmediate(clone);}
        }
        [Test]
        public void EvolutionCyclesAndMissingVisualsAreRejected()
        {
            var source=Resources.Load<RosterData>("GameRoster");var roster=UnityEngine.Object.Instantiate(source);
            var a=UnityEngine.Object.Instantiate(source.Pokemon[0]);var b=UnityEngine.Object.Instantiate(source.Pokemon[1]);
            try
            {
                b.Cost=a.Cost;roster.Pokemon=new[]{a,b};roster.ShopPokemon=new[]{a};
                a.Evolutions=new[]{new EvolutionData{Target=b}};b.Evolutions=new[]{new EvolutionData{Target=a}};
                Assert.Throws<InvalidOperationException>(()=>roster.Validate());
                a.Evolutions=Array.Empty<EvolutionData>();b.Evolutions=Array.Empty<EvolutionData>();a.Visual=null;
                Assert.Throws<InvalidOperationException>(()=>roster.Validate());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(a);UnityEngine.Object.DestroyImmediate(b);UnityEngine.Object.DestroyImmediate(roster);
            }
        }
        [Test]
        public void PresentationUsesAuthoredNamesAndClefairySprites()
        {
            Assert.That(GameContent.DisplayName("geodude"),Is.EqualTo("삐삐"));
            Assert.That(GameContent.DisplayName("follow-me"),Is.EqualTo("날따름"));
            Assert.That(GameContent.DisplayName("shadow-ball"),Is.EqualTo("섀도볼"));
            var library=PokeChess.Client.Data.PokemonAnimationLibrary.Load();
            Assert.That(library.Find("geodude","Idle").PokemonId,Is.EqualTo("clefairy"));
        }
    }
}
