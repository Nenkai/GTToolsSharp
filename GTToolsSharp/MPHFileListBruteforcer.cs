using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;

using GTToolsSharp.Volumes;
using GTToolsSharp.Entities;

namespace GTToolsSharp;

internal class MPHFileListBruteforcer
{
    private GTVolumeMPH _vol;

    public MPHFileListBruteforcer(GTVolumeMPH volume)
    {
        _vol = volume;
    }

    public void BruteforceFindFiles(SortedDictionary<string, MPHNodeInfo> validFiles)
    {
        StreamWriter sw = new StreamWriter("bruteforced_files.txt");

        BruteforceLiveries(validFiles);
        BruteforceGameParameters(validFiles);

        for (var i = 0; i < 100; i++)
        {
            _vol.CheckFile(validFiles, $"piece/4k/championship_logo/championship_{i.ToString().PadLeft(2, '0')}.img");
            _vol.CheckFile(validFiles, $"piece/4k/championship_logo_L/championship_{i.ToString().PadLeft(2, '0')}.img");

            string idPad = i.ToString().PadLeft(2, '0');
            _vol.CheckFile(validFiles, $"common/avatar/4k/met/mf{idPad}.img");
            _vol.CheckFile(validFiles, $"piece/4k/avatar/storemet/mf{idPad}.img");
            _vol.CheckFile(validFiles, $"piece/4k/avatar/sidemet/mf{idPad}.img");
            _vol.CheckFile(validFiles, $"piece/4k/avatar/suit/ma{idPad}.img");

            _vol.CheckFile(validFiles, $"common/avatar/4k/met/mf{idPad}.img");
            _vol.CheckFile(validFiles, $"piece/4k/storemet/mf{idPad}.img");
            _vol.CheckFile(validFiles, $"piece/4k/sidemet/mf{idPad}.img");
            _vol.CheckFile(validFiles, $"piece/4k/suit/ma{idPad}.img");

            for (var variation = 0; variation < 100; variation++)
            {
                string varPad = variation.ToString().PadLeft(2, '0');
                _vol.CheckFile(validFiles, $"common/avatar/4k/met/mf{idPad}_{varPad}.img");
                _vol.CheckFile(validFiles, $"piece/4k/avatar/storemet/mf{idPad}_{varPad}.img");
                _vol.CheckFile(validFiles, $"piece/4k/avatar/sidemet/mf{idPad}_{varPad}.img");
                _vol.CheckFile(validFiles, $"piece/4k/avatar/suit/ma{idPad}_{varPad}.img");
                _vol.CheckFile(validFiles, $"piece/4k/storemet/mf{idPad}_{varPad}.img");
                _vol.CheckFile(validFiles, $"piece/4k/sidemet/mf{idPad}_{varPad}.img");
                _vol.CheckFile(validFiles, $"piece/4k/suit/ma{idPad}_{varPad}.img");
            }
        }

        for (var i = 0; i < 1000; i++)
        {
            _vol.CheckFile(validFiles, $"common/vfx/fireworks/{i}/fireworks");
        }

        for (var id = 0; id < 15000; id++)
        {
            // frame_id
            _vol.CheckFile(validFiles, $"piece/raw/4k/event_img/{id}.img");

            string p = $"livery/decal";

            _vol.CheckFile(validFiles, p + $"/img/{id}.jpg");
            _vol.CheckFile(validFiles, p + $"/img/{id}.svg");
            _vol.CheckFile(validFiles, p + $"/img/{id}.img");

            _vol.CheckFile(validFiles, p + $"/txs/{id}.jpg");
            _vol.CheckFile(validFiles, p + $"/txs/{id}.txs");
        }

        // Car Models
        BruteforceCarFiles(validFiles);
        BruteforceCarPartFiles(validFiles);
        BruteforceTireFiles(validFiles);
        BruteforceCrowdFiles(validFiles);

        _vol.CheckFile(validFiles, $"character/md01");
        for (var id = 0; id < 10000; id++)
        {
            string pad3 = id.ToString().PadLeft(3, '0');
            _vol.CheckFile(validFiles, $"character/f{pad3}");
            _vol.CheckFile(validFiles, $"character/f{pad3}s");
            _vol.CheckFile(validFiles, $"character/hm{pad3}");
            _vol.CheckFile(validFiles, $"character/hm{pad3}s");
            _vol.CheckFile(validFiles, $"character/hs{pad3}");
            _vol.CheckFile(validFiles, $"character/hs{pad3}s");
            _vol.CheckFile(validFiles, $"character/o{pad3}");
            _vol.CheckFile(validFiles, $"character/o{pad3}s");
            _vol.CheckFile(validFiles, $"character/op{pad3}");
            _vol.CheckFile(validFiles, $"character/op{pad3}s");

            string pad2 = id.ToString().PadLeft(2, '0');
            _vol.CheckFile(validFiles, $"character/mf{pad2}");
            _vol.CheckFile(validFiles, $"character/ma{pad2}");
            _vol.CheckFile(validFiles, $"character/mf{pad2}s");
            _vol.CheckFile(validFiles, $"character/ma{pad2}s");
            _vol.CheckFile(validFiles, $"character/tex{id.ToString().PadLeft(4, '0')}");
        }

        // AdTex + Sky
        for (var id = 0; id < 1000; id++)
        {
            _vol.CheckFile(validFiles, $"sky/{id}/sky");
            _vol.CheckFile(validFiles, $"sky/{id}/envptr");
            _vol.CheckFile(validFiles, $"adtex/adtex_{id}.txs");
        }

        // Tracks
        _vol.CheckFile(validFiles, "crs/gadgets");

        for (var course_id = 0; course_id < 700; course_id++)
        {
            _vol.CheckFile(validFiles, $"develop/synthetic_sky/cloud/{course_id}/cloud.txs");
            _vol.CheckFile(validFiles, $"develop/synthetic_sky/stratus/{course_id}.txs");
        }

        for (var course_id = 0; course_id < 700; course_id++)
        {
            BruteforceTrackType(validFiles, "c", course_id);
            BruteforceTrackType(validFiles, "p", course_id);
            BruteforceTrackType(validFiles, "t", course_id);
        }

        // Scapes
        BruteforceScapesFiles(validFiles);
        BruteforceParticleFiles(validFiles);

        // Sndz
        BruteforceCarSound(validFiles);

        // Rtext
        string[] countries = ["BP", "CN", "CZ", "DK", "DE", "EL", "ES", "FI", "FR", "GB", "HU", "IT", "JP", "KR", "MS", "NO", "NL", "PL", "PT", "RU", "SE", "TR", "TW", "US"];
        foreach (var c in countries)
        {
            _vol.CheckFile(validFiles, $"rtext/common/{c}.rt2");
            _vol.CheckFile(validFiles, $"rtext/patch/{c}.rt2");
            _vol.CheckFile(validFiles, $"common/banner_{c}.txs");
        }


        for (var y = 1970; y < 2030; y++)
        {
            for (var m = 0; m < 31; m++)
            {
                for (var d = 0; d < 31; d++)
                {
                    _vol.CheckFile(validFiles, $"carshop/used_car_dealer/{y}{m.ToString().PadLeft(2, '0')}{d.ToString().PadLeft(2, '0')}.json".ToLower());
                    _vol.CheckFile(validFiles, $"carshop/brand_central_dealer/{y}{m.ToString().PadLeft(2, '0')}{d.ToString().PadLeft(2, '0')}.json".ToLower());
                    _vol.CheckFile(validFiles, $"carshop/legend_car_dealer/{y}{m.ToString().PadLeft(2, '0')}{d.ToString().PadLeft(2, '0')}.json".ToLower());

                }
            }
        }

        for (var y = 0; y < 1000; y++)
        {
            _vol.CheckFile(validFiles, $"carshop/used_car_dealer/CC_{y}.json".ToLower());
        }

        var l = validFiles.Where(e => e.Key.StartsWith("carshop")).ToList();

        if (File.Exists("bruteforced_files.txt"))
        {
            Console.WriteLine("[!] Overwrite Bruteforced GT7 file list (bruteforced_files.txt)? [y/n]");
            var overwrite = Console.ReadLine() == "y";
            if (!overwrite)
                return;
        }

        foreach (var f in validFiles)
            sw.WriteLine(f.Key);
    }

    private void BruteforceLiveries(SortedDictionary<string, MPHNodeInfo> validFiles)
    {
        // 1.00 matches for int.MaxValue
        CheckStyleIdSpecial(validFiles, 954314528);
        CheckStyleIdSpecial(validFiles, 84496325);
        CheckStyleIdSpecial(validFiles, 1260526334);

        // 1.13 - matches for uint.maxvalue
        CheckStyleIdSpecial(validFiles, 169923064);

        ulong styleIdStart = 1152921504606846991 - 10000; // First from StyleList minus 10000
        for (var id = styleIdStart; id < styleIdStart + 40000; id++)
        {
            _vol.CheckFile(validFiles, $"livery/style/{id}/cp.json");
            _vol.CheckFile(validFiles, $"livery/style/{id}/style_0.json");
            _vol.CheckFile(validFiles, $"livery/style/{id}/style_edit_9.json");
            _vol.CheckFile(validFiles, $"livery/style/{id}/4k_r.dat");
            _vol.CheckFile(validFiles, $"livery/style/{id}/4k_4.dat");
            _vol.CheckFile(validFiles, $"livery/style/{id}/2k_5.dat");
            _vol.CheckFile(validFiles, $"livery/style/{id}/1k_6.dat");
            _vol.CheckFile(validFiles, $"livery/style/{id}/256_7.dat");
            _vol.CheckFile(validFiles, $"livery/style/{id}/128_8.dat");
            _vol.CheckFile(validFiles, $"livery/style/{id}/odeko.dat");
            _vol.CheckFile(validFiles, $"livery/style/{id}/11.jpg");
            _vol.CheckFile(validFiles, $"livery/style/{id}/12.jpg");
            _vol.CheckFile(validFiles, $"livery/style/{id}/23.jpg");
            _vol.CheckFile(validFiles, $"livery/style/{id}/24.png");
            _vol.CheckFile(validFiles, $"livery/style/{id}/25.png");
            _vol.CheckFile(validFiles, $"livery/style/{id}/origin.svg");
            _vol.CheckFile(validFiles, $"livery/style/{id}/thumb_side_3.png");
            _vol.CheckFile(validFiles, $"livery/style/{id}/filemapping.json");
            _vol.CheckFile(validFiles, $"livery/style/{id}/edit.json");
            _vol.CheckFile(validFiles, $"livery/style/{id}/set_0.json");
            _vol.CheckFile(validFiles, $"livery/style/{id}/thumb_2.png");
            _vol.CheckFile(validFiles, $"livery/style/{id}/2k_3.dat");
            _vol.CheckFile(validFiles, $"livery/style/{id}/1k_4.dat");
            _vol.CheckFile(validFiles, $"livery/style/{id}/livery_edit_5.json");
            _vol.CheckFile(validFiles, $"livery/style/{id}/7.jpg");
            _vol.CheckFile(validFiles, $"livery/style/{id}/8.jpg");
            _vol.CheckFile(validFiles, $"livery/style/{id}/thumb_side.png");
            _vol.CheckFile(validFiles, $"livery/style/{id}/6.jpg");
            _vol.CheckFile(validFiles, $"livery/style/{id}/set.json");
            _vol.CheckFile(validFiles, $"livery/style/{id}/2k.dat");
            _vol.CheckFile(validFiles, $"livery/style/{id}/1k.dat");
            _vol.CheckFile(validFiles, $"livery/style/{id}/thumbnail.png");
            _vol.CheckFile(validFiles, $"livery/style/{id}/progress.json");
            _vol.CheckFile(validFiles, $"livery/style/{id}/used.jpg");

            // _vol.CheckFile(validFiles, $"livery/livery_set/{id}/3.dat");
            // _vol.CheckFile(validFiles, $"livery/livery_set/{id}/4.dat");
            // 5 6 and 7.. maybe?
        }

        styleIdStart = 0x1000000000000000;
        for (var id = styleIdStart - 0x1000000000000000; id < 20000; id++)
        {
            CheckStyleIdSpecial(validFiles, id);
        }
    }

    private void CheckStyleIdSpecial(SortedDictionary<string, MPHNodeInfo> validFiles, ulong actualFileId)
    {
        _vol.CheckFile(validFiles, $"livery/style/10000000/{actualFileId}/cp.json");
        _vol.CheckFile(validFiles, $"livery/style/10000000/{actualFileId}/style_0.json");
        _vol.CheckFile(validFiles, $"livery/style/10000000/{actualFileId}/style_edit_9.json");
        _vol.CheckFile(validFiles, $"livery/style/10000000/{actualFileId}/4k_r.dat");
        _vol.CheckFile(validFiles, $"livery/style/10000000/{actualFileId}/4k_4.dat");
        _vol.CheckFile(validFiles, $"livery/style/10000000/{actualFileId}/2k_5.dat");
        _vol.CheckFile(validFiles, $"livery/style/10000000/{actualFileId}/1k_6.dat");
        _vol.CheckFile(validFiles, $"livery/style/10000000/{actualFileId}/256_7.dat");
        _vol.CheckFile(validFiles, $"livery/style/10000000/{actualFileId}/128_8.dat");
        _vol.CheckFile(validFiles, $"livery/style/10000000/{actualFileId}/odeko.dat");
        _vol.CheckFile(validFiles, $"livery/style/10000000/{actualFileId}/11.jpg");
        _vol.CheckFile(validFiles, $"livery/style/10000000/{actualFileId}/12.jpg");
        _vol.CheckFile(validFiles, $"livery/style/10000000/{actualFileId}/23.jpg");
        _vol.CheckFile(validFiles, $"livery/style/10000000/{actualFileId}/24.png");
        _vol.CheckFile(validFiles, $"livery/style/10000000/{actualFileId}/25.png");
        _vol.CheckFile(validFiles, $"livery/style/10000000/{actualFileId}/origin.svg");
        _vol.CheckFile(validFiles, $"livery/style/10000000/{actualFileId}/thumb_side_3.png");
        _vol.CheckFile(validFiles, $"livery/style/10000000/{actualFileId}/filemapping.json");
        _vol.CheckFile(validFiles, $"livery/style/10000000/{actualFileId}/edit.json");
        _vol.CheckFile(validFiles, $"livery/style/10000000/{actualFileId}/set_0.json");
        _vol.CheckFile(validFiles, $"livery/style/10000000/{actualFileId}/thumb_2.png");
        _vol.CheckFile(validFiles, $"livery/style/10000000/{actualFileId}/2k_3.dat");
        _vol.CheckFile(validFiles, $"livery/style/10000000/{actualFileId}/1k_4.dat");
        _vol.CheckFile(validFiles, $"livery/style/10000000/{actualFileId}/livery_edit_5.json");
        _vol.CheckFile(validFiles, $"livery/style/10000000/{actualFileId}/7.jpg");
        _vol.CheckFile(validFiles, $"livery/style/10000000/{actualFileId}/8.jpg");
        _vol.CheckFile(validFiles, $"livery/style/10000000/{actualFileId}/thumb_side.png");
        _vol.CheckFile(validFiles, $"livery/style/10000000/{actualFileId}/6.jpg");
        _vol.CheckFile(validFiles, $"livery/style/10000000/{actualFileId}/set.json");
        _vol.CheckFile(validFiles, $"livery/style/10000000/{actualFileId}/2k.dat");
        _vol.CheckFile(validFiles, $"livery/style/10000000/{actualFileId}/1k.dat");
        _vol.CheckFile(validFiles, $"livery/style/10000000/{actualFileId}/thumbnail.png");
        _vol.CheckFile(validFiles, $"livery/style/10000000/{actualFileId}/progress.json");
        _vol.CheckFile(validFiles, $"livery/style/10000000/{actualFileId}/used.jpg");
    }

    private void BruteforceGameParameters(SortedDictionary<string, MPHNodeInfo> validFiles)
    {
        // Check game parameters
        for (var event_id = 1000000; event_id < 1050000; event_id++)
        {
            _vol.CheckFile(validFiles, $"game_parameter/gp/{event_id}.json");
            _vol.CheckFile(validFiles, $"replay/license/{event_id}.dat");
            _vol.CheckFile(validFiles, $"replay/demo/{event_id}.dat");
        }

        for (var preset_weather_id = 0; preset_weather_id < 30; preset_weather_id++)
        {
            string idPad = preset_weather_id.ToString().PadLeft(2, '0');
            _vol.CheckFile(validFiles, $"game_parameter/S{idPad}.json");
            _vol.CheckFile(validFiles, $"game_parameter/C{idPad}.json");
            _vol.CheckFile(validFiles, $"game_parameter/R{idPad}.json");

            _vol.CheckFile(validFiles, $"piece/4k/weather_image/S{idPad}.img");
            _vol.CheckFile(validFiles, $"piece/4k/weather_image/C{idPad}.img");
            _vol.CheckFile(validFiles, $"piece/4k/weather_image/R{idPad}.img");

            _vol.CheckFile(validFiles, $"piece/4k/weather_thumbnail/S{idPad}.img");
            _vol.CheckFile(validFiles, $"piece/4k/weather_thumbnail/C{idPad}.img");
            _vol.CheckFile(validFiles, $"piece/4k/weather_thumbnail/R{idPad}.img");

            _vol.CheckFile(validFiles, $"piece/4k/icon/weather/S{idPad}.img");
            _vol.CheckFile(validFiles, $"piece/4k/icon/weather/C{idPad}.img");
            _vol.CheckFile(validFiles, $"piece/4k/icon/weather/R{idPad}.img");
        }
    }

    private void BruteforceCarSound(SortedDictionary<string, MPHNodeInfo> validFiles)
    {
        for (var i = 0; i < 100000; i++)
        {
            _vol.CheckFile(validFiles, $"carsound/aes/{i}");
            _vol.CheckFile(validFiles, $"carsound/gtes2/{i}/{i}.gtesd1");
            _vol.CheckFile(validFiles, $"carsound/gtes2/{i}/{i}.gtesd2");
            _vol.CheckFile(validFiles, $"carsound/start/{i.ToString().PadLeft(5, '0')}.szd3");
        }
    }

    private void BruteforceCrowdFiles(SortedDictionary<string, MPHNodeInfo> validFiles)
    {
        for (var id = 0; id < 1000000; id += 100)
        {
            _vol.CheckFile(validFiles, $"crowd/pos/{id.ToString().PadLeft(6, '0')}.pos");
        }

        for (var id = 0; id < 10000; id++)
        {
            _vol.CheckFile(validFiles, $"crowd/amsx/{id.ToString().PadLeft(4, '0')}.amsx");
        }
    }

    private void BruteforceCarFiles(SortedDictionary<string, MPHNodeInfo> validFiles)
    {
        for (var make = 0; make < 500; make++)
        {
            for (var id = 0; id < 500; id++)
            {
                string carPath = $"car/{make.ToString().PadLeft(4, '0')}/{id.ToString().PadLeft(4, '0')}";
                _vol.CheckFile(validFiles, carPath + "/meter");
                _vol.CheckFile(validFiles, carPath + "/interior");
                _vol.CheckFile(validFiles, carPath + "/meter.sepdat");
                _vol.CheckFile(validFiles, carPath + "/interior.sepdat");
                _vol.CheckFile(validFiles, carPath + "/race/body");
                _vol.CheckFile(validFiles, carPath + "/hq/body");
                _vol.CheckFile(validFiles, carPath + "/race/body.sepdat");
                _vol.CheckFile(validFiles, carPath + "/hq/body.sepdat");
                _vol.CheckFile(validFiles, carPath + "/race/wheel");
                _vol.CheckFile(validFiles, carPath + "/hq/wheel");
                _vol.CheckFile(validFiles, carPath + "/race/brakedisk");
                _vol.CheckFile(validFiles, carPath + "/hq/brakedisk");
                _vol.CheckFile(validFiles, carPath + "/race/caliper");
                _vol.CheckFile(validFiles, carPath + "/hq/caliper");
                _vol.CheckFile(validFiles, carPath + "/info");
                _vol.CheckFile(validFiles, carPath + "/tirehouse_sdf_normal");
                _vol.CheckFile(validFiles, carPath + "/tirehouse_sdf_wide");
                _vol.CheckFile(validFiles, carPath + "/window_stencil");

                // Thumbnails
                for (var variation = 0; variation < 30; variation++)
                {
                    _vol.CheckFile(validFiles, carPath + $"/thumbnail/used_{variation.ToString().PadLeft(2, '0')}.jpg");
                    _vol.CheckFile(validFiles, carPath + $"/thumbnail/side_{variation.ToString().PadLeft(2, '0')}.img");
                    _vol.CheckFile(validFiles, carPath + $"/thumbnail/73_{variation.ToString().PadLeft(2, '0')}.img");
                }
            }
        }

        for (var id = 0; id < 10; id++)
        {
            // This is a flag list but just bruteforce it that way, lazy
            for (var char1 = 0; char1 < 255; char1++)
            {
                for (var char2 = 0; char2 < 255; char2++)
                {
                    _vol.CheckFile(validFiles, $"car/hq/{id}/{(char)char1}{(char)char2}.img");
                }
                
            }
            
        }
    }

    private void BruteforceCarPartFiles(SortedDictionary<string, MPHNodeInfo> validFiles)
    {
        for (var id = 0; id < 100; id++)
        {
            string idPad = id.ToString().PadLeft(4, '0');
            _vol.CheckFile(validFiles, $"carparts/brakedisk/{idPad}/hq/brakedisk.sepdat");
            _vol.CheckFile(validFiles, $"carparts/brakedisk/{idPad}/race/brakedisk");
            _vol.CheckFile(validFiles, $"carparts/caliper/{idPad}/hq/caliper.sepdat");
            _vol.CheckFile(validFiles, $"carparts/caliper/{idPad}/race/caliper");


            for (var num = 0; num < 10000; num++)
            {
                // ID from WINGLET
                _vol.CheckFile(validFiles, $"carparts/winglet/{idPad}/hq/Winglet{num.ToString().PadLeft(4, '0')}");
                _vol.CheckFile(validFiles, $"carparts/winglet/{idPad}/race/Winglet{num.ToString().PadLeft(4, '0')}");
            }
        }

        for (var i = 0; i < 1000; i++)
        {
            _vol.CheckFile(validFiles, $"carparts/caliper/thumbnail/color_{i.ToString().PadLeft(4, '0')}.img");
        }

        for (var id = 0; id < 1000; id++)
        {
            string idPad = id.ToString().PadLeft(3, '0');

            _vol.CheckFile(validFiles, $"carparts/race/he{idPad}.img");
            _vol.CheckFile(validFiles, $"carparts/hq/he{idPad}.img");

            _vol.CheckFile(validFiles, $"carparts/image/race/tuner_{idPad}.cdk");
            _vol.CheckFile(validFiles, $"carparts/image/hq/tuner_{idPad}.cdk");

            // From ExchangeWHEEL
            for (var variation = 0; variation < 10; variation++)
            {
                string variationPad = variation.ToString().PadLeft(2, '0');
                _vol.CheckFile(validFiles, $"carparts/thumbnail/4k/he{idPad}_{variationPad}.img");
            }
        }

        for (var id = 0; id < 100; id++)
        {
            string idPad = id.ToString().PadLeft(2, '0');

            // GetColorClusterThumbnail
            _vol.CheckFile(validFiles, $"carparts/colorcluster/{idPad}.img");

            // This is a string, not an id, but who cares
            _vol.CheckFile(validFiles, $"carparts/image/race/decken{idPad}.cdk");
            _vol.CheckFile(validFiles, $"carparts/image/hq/decken{idPad}.cdk");
            _vol.CheckFile(validFiles, $"carparts/image/race/deckenn{idPad}.cdk");
            _vol.CheckFile(validFiles, $"carparts/image/hq/deckenn{idPad}.cdk");
            _vol.CheckFile(validFiles, $"carparts/image/race/deckenm{idPad}.cdk");
            _vol.CheckFile(validFiles, $"carparts/image/hq/deckenm{idPad}.cdk");

            _vol.CheckFile(validFiles, $"carparts/image/race/window{idPad}.cws");
            _vol.CheckFile(validFiles, $"carparts/image/hq/window{idPad}.cws");

        }

        for (var id = 0; id < 5000; id++)
        {
            _vol.CheckFile(validFiles, $"carparts/specialpaint/tile/{id}.img");
            _vol.CheckFile(validFiles, $"carparts/specialpaint/whole/{id}.img");
        }
    }

    private void BruteforceTireFiles(SortedDictionary<string, MPHNodeInfo> validFiles)
    {
        for (var id = 0; id < 3000; id++)
        {
            string idPad = id.ToString().PadLeft(4, '0');
            _vol.CheckFile(validFiles, $"tire/race/m{idPad}");
            _vol.CheckFile(validFiles, $"tire/hq/m{idPad}");

            for (var i = 0; i < 100; i++)
            {
                _vol.CheckFile(validFiles, $"tire/race/m{idPad}_{i.ToString().PadLeft(2, '0')}");
                _vol.CheckFile(validFiles, $"tire/hq/m{idPad}_{i.ToString().PadLeft(2, '0')}");
            }
        }

    }

    private void BruteforceScapesFiles(SortedDictionary<string, MPHNodeInfo> validFiles)
    {
        for (var registId = 0; registId < 5000; registId++)
        {
            Search(registId, $"scapes/{registId}");

            for (var variation = 0; variation < 10; variation++)
            {
                Search(registId, $"scapes/{registId}/v{variation.ToString().PadLeft(2, '0')}");
                Search(registId, $"scapes/{registId}/m{variation.ToString().PadLeft(2, '0')}");
                Search(registId, $"scapes/{registId}/e{variation.ToString().PadLeft(2, '0')}");
            }
        }

        string p = $"scapes/curation";
        for (var j = 0; j < 10000; j++)
        {
            _vol.CheckFile(validFiles, $"scapes/slide/{j}.img");
            _vol.CheckFile(validFiles, $"scapes/slide/{j}.gxl");

            _vol.CheckFile(validFiles, p + $"/{j}.img");
            _vol.CheckFile(validFiles, p + $"/map_{j}.img");

            _vol.CheckFile(validFiles, p + $"/{j}.jpg");
            _vol.CheckFile(validFiles, p + $"/menu_{j}.png");

            for (var k = 0; k < 100; k++)
            {
                _vol.CheckFile(validFiles, p + $"/menu_{j}_{k}.jpg");
            }
        }

        void Search(int registId, string startPath)
        {
            _vol.CheckFile(validFiles, startPath + $"/script.txt");
            _vol.CheckFile(validFiles, startPath + $"/script_ext.txt");
            _vol.CheckFile(validFiles, startPath + $"/script_stage.txt");
            _vol.CheckFile(validFiles, startPath + $"/specular_env.txs");
            _vol.CheckFile(validFiles, startPath + $"/floor.runway");
            _vol.CheckFile(validFiles, startPath + $"/ibf.txs");
            _vol.CheckFile(validFiles, startPath + $"/diffuse_env.txs");
            _vol.CheckFile(validFiles, startPath + $"/thumbnail.jpg");
            _vol.CheckFile(validFiles, startPath + $"/background");
            _vol.CheckFile(validFiles, startPath + $"/serialize.ssb");
            _vol.CheckFile(validFiles, startPath + $"/scapes.kvp");
            _vol.CheckFile(validFiles, startPath + $"/scapes_s.kvp");
            _vol.CheckFile(validFiles, startPath + $"/disc_logo.txs");
            _vol.CheckFile(validFiles, startPath + $"/disc_pattern.txs");
            _vol.CheckFile(validFiles, startPath + $"/floor.mdl");
            _vol.CheckFile(validFiles, startPath + $"/bg.mdl");
            _vol.CheckFile(validFiles, startPath + $"/restore.json");

            _vol.CheckFile(validFiles, startPath + $"/env.mdl");
            _vol.CheckFile(validFiles, startPath + $"/env1.mdl");
            _vol.CheckFile(validFiles, startPath + $"/env2.mdl");
            _vol.CheckFile(validFiles, startPath + $"/env3.mdl");

            _vol.CheckFile(validFiles, startPath + $"Confetti_01.mdl");
            _vol.CheckFile(validFiles, startPath + $"B_4th_Parcferme.evs");
            _vol.CheckFile(validFiles, startPath + $"Roulette_CarDelivery.evs");
            _vol.CheckFile(validFiles, startPath + $"UsedCar_Purchase01.evs");
            _vol.CheckFile(validFiles, startPath + $"UsedCar_Purchase02.evs");
            _vol.CheckFile(validFiles, startPath + $"UsedCar_Purchase03.evs");
        }
    }

    private void BruteforceParticleFiles(SortedDictionary<string, MPHNodeInfo> validFiles)
    {
        for (var v = 0; v < 100; v++)
        {
            string p = $"common/vfx/particle_system/quick_fix";
            string pad = v.ToString().PadLeft(2, '0');

            _vol.CheckFile(validFiles, p + $"/shader/spark_shader_v{pad}.dat");
            _vol.CheckFile(validFiles, p + $"/shader/smoke_shader_v{pad}.dat");
            _vol.CheckFile(validFiles, p + $"/shader/thermal_shader_v{pad}.dat");
            _vol.CheckFile(validFiles, p + $"/shader/sediment_shader_v{pad}.dat");
            _vol.CheckFile(validFiles, p + $"/shader/template_shader_v{pad}.dat");
            _vol.CheckFile(validFiles, p + $"/shader/rainfall_shader_v{pad}.dat");
            _vol.CheckFile(validFiles, p + $"/shader/afterfire_shader_v{pad}.dat");
            _vol.CheckFile(validFiles, p + $"/shader/fireworks_shader_v{pad}.dat");
            _vol.CheckFile(validFiles, p + $"/shader/skyrockets_shader_v{pad}.dat");
            _vol.CheckFile(validFiles, p + $"/shader/rainsplash_shader_v{pad}.dat");
            _vol.CheckFile(validFiles, p + $"/shader/water_crawl_shader_v{pad}.dat");
            _vol.CheckFile(validFiles, p + $"/shader/water_plane_shader_v{pad}.dat");
            _vol.CheckFile(validFiles, p + $"/shader/crash_debris_shader_v{pad}.dat");
            _vol.CheckFile(validFiles, p + $"/shader/puddle_water_shader_v{pad}.dat");

            _vol.CheckFile(validFiles, p + $"/effect/water_plane_v{pad}.dat");
            _vol.CheckFile(validFiles, p + $"/effect/water_crawl_v{pad}.dat");
            _vol.CheckFile(validFiles, p + $"/effect/template_v{pad}.dat");
            _vol.CheckFile(validFiles, p + $"/effect/thermal_v{pad}.dat");
            _vol.CheckFile(validFiles, p + $"/effect/spark_v{pad}.dat");
            _vol.CheckFile(validFiles, p + $"/effect/smoke_v{pad}.dat");
            _vol.CheckFile(validFiles, p + $"/effect/skyrockets_v{pad}.dat");
            _vol.CheckFile(validFiles, p + $"/effect/sediment_v{pad}.dat");
            _vol.CheckFile(validFiles, p + $"/effect/rainsplash_v{pad}.dat");
            _vol.CheckFile(validFiles, p + $"/effect/rainfall_v{pad}.dat");
            _vol.CheckFile(validFiles, p + $"/effect/puddle_water_v{pad}.dat");
            _vol.CheckFile(validFiles, p + $"/effect/fireworks_v{pad}.dat");
            _vol.CheckFile(validFiles, p + $"/effect/crash_debris_v{pad}.dat");
            _vol.CheckFile(validFiles, p + $"/effect/afterfire_v{pad}.dat");
        }
    }

    private void BruteforceTrackType(SortedDictionary<string, MPHNodeInfo> validFiles, string type, int course_id)
    {
        _vol.CheckFile(validFiles, $"sound_gt/crs/{type}{course_id.ToString().PadLeft(3, '0')}/sound");

        _vol.CheckFile(validFiles, $"crs/{type}{course_id.ToString().PadLeft(3, '0')}.shapestream");
        _vol.CheckFile(validFiles, $"crs/{type}{course_id.ToString().PadLeft(3, '0')}.texstream");

        string crsPath = $"crs/{type}{course_id.ToString().PadLeft(3, '0')}";
        _vol.CheckFile(validFiles, crsPath + "/envptr");
        _vol.CheckFile(validFiles, crsPath + "/crowd_envptr");
        _vol.CheckFile(validFiles, crsPath + "/pack");
        _vol.CheckFile(validFiles, crsPath + "/pack_s");
        _vol.CheckFile(validFiles, crsPath + "/maxsizes");
        _vol.CheckFile(validFiles, crsPath + "/pack");
        _vol.CheckFile(validFiles, crsPath + "/probe_gbuffers_common.img");
        for (var i = 0; i < 10; i++)
            _vol.CheckFile(validFiles, crsPath + $"/probe_gbuffers_ev{i}.img");

        _vol.CheckFile(validFiles, crsPath + "/tree.mdl");
        _vol.CheckFile(validFiles, crsPath + "/tree_layout.bin");
        _vol.CheckFile(validFiles, crsPath + "/grass_mat.mdl");
        _vol.CheckFile(validFiles, crsPath + "/grass_tex.img");

        _vol.CheckFile(validFiles, crsPath + "/synthetic_sky_envptr");
        _vol.CheckFile(validFiles, crsPath + "/shape_stream");
        _vol.CheckFile(validFiles, crsPath + "/tex_stream");
        _vol.CheckFile(validFiles, crsPath + "/build_info");

        for (var layout_no = 0; layout_no < 20; layout_no++)
        {

            string crslayoutPath = crsPath + $"/L{layout_no.ToString().PadLeft(2, '0')}";

            _vol.CheckFile(validFiles, crslayoutPath + "/course_map");
            _vol.CheckFile(validFiles, crslayoutPath + "/course_offset");
            _vol.CheckFile(validFiles, crslayoutPath + "/parking");
            _vol.CheckFile(validFiles, crslayoutPath + "/replay_offset");
            _vol.CheckFile(validFiles, crslayoutPath + "/spots.json");
            _vol.CheckFile(validFiles, crslayoutPath + "/road_condition_map");

            _vol.CheckFile(validFiles, crslayoutPath + "/auto_drive");
            _vol.CheckFile(validFiles, crslayoutPath + "/course_line");
            _vol.CheckFile(validFiles, crslayoutPath + "/driving_marker");
            _vol.CheckFile(validFiles, crslayoutPath + "/enemy_spline");
            _vol.CheckFile(validFiles, crslayoutPath + "/expert_line");
            _vol.CheckFile(validFiles, crslayoutPath + "/gadget_layout");
            _vol.CheckFile(validFiles, crslayoutPath + "/pack");
            _vol.CheckFile(validFiles, crslayoutPath + "/penalty_line");
            _vol.CheckFile(validFiles, crslayoutPath + "/replay");
            _vol.CheckFile(validFiles, crslayoutPath + "/runway");

            _vol.CheckFile(validFiles, crslayoutPath + "/csch_layout.evs");
            _vol.CheckFile(validFiles, crslayoutPath + "/vfxloc");
            _vol.CheckFile(validFiles, crslayoutPath + "/csch_animobj.evs");
        }
    }
}
